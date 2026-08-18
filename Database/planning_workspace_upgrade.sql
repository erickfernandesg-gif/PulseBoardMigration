-- Planejamento corporativo: baseline transacional, dependencias sem ciclos e recorrencias idempotentes.
-- Execute depois de board_operations_integration_fix.sql.

begin;

alter table public.project_baselines add column if not exists is_active boolean not null default false;

alter table public.recurring_task_rules add column if not exists time_zone text not null default 'America/Sao_Paulo';
alter table public.recurring_task_rules add column if not exists target_status text not null default 'todo';
alter table public.recurring_task_rules add column if not exists priority text not null default 'medium';
alter table public.recurring_task_rules add column if not exists ends_at timestamptz;
alter table public.recurring_task_rules add column if not exists max_occurrences integer;
alter table public.recurring_task_rules add column if not exists occurrences_created integer not null default 0;
alter table public.recurring_task_rules add column if not exists updated_at timestamptz not null default now();

alter table public.tasks add column if not exists recurring_rule_id uuid references public.recurring_task_rules(id) on delete set null;
alter table public.tasks add column if not exists recurrence_scheduled_at timestamptz;

with latest as (
  select id,row_number() over(partition by board_id order by version desc,created_at desc,id desc) as position
  from public.project_baselines
)
update public.project_baselines baseline set is_active=(latest.position=1)
from latest where latest.id=baseline.id;

do $$
begin
  if not exists (select 1 from pg_constraint where conname='recurring_task_rules_priority_ck' and conrelid='public.recurring_task_rules'::regclass) then
    alter table public.recurring_task_rules add constraint recurring_task_rules_priority_ck
      check(priority in ('low','medium','high','critical'));
  end if;
  if not exists (select 1 from pg_constraint where conname='recurring_task_rules_max_occurrences_ck' and conrelid='public.recurring_task_rules'::regclass) then
    alter table public.recurring_task_rules add constraint recurring_task_rules_max_occurrences_ck
      check(max_occurrences is null or max_occurrences between 1 and 10000);
  end if;
end $$;

create unique index if not exists project_baselines_one_active_idx
  on public.project_baselines(board_id) where is_active;
create unique index if not exists tasks_recurring_occurrence_uq
  on public.tasks(recurring_rule_id,recurrence_scheduled_at) where recurring_rule_id is not null;
create index if not exists recurring_task_rules_active_due_idx
  on public.recurring_task_rules(next_run_at,id) where is_active;
create index if not exists recurring_task_rules_board_idx on public.recurring_task_rules(board_id);
create index if not exists task_templates_board_active_idx on public.task_templates(board_id,name) where is_active;
create index if not exists task_templates_team_active_idx on public.task_templates(team_id,name) where is_active;
create index if not exists tasks_recurring_rule_idx on public.tasks(recurring_rule_id) where recurring_rule_id is not null;

drop policy if exists holidays_manage on public.company_holidays;
create policy holidays_manage on public.company_holidays for all to authenticated
  using((select public.is_admin()) or team_id=(select public.current_team_id()))
  with check((select public.is_admin()) or team_id=(select public.current_team_id()));

create or replace function public.capture_project_baseline(p_board_id uuid,p_name text default null)
returns uuid language plpgsql security invoker set search_path='' as $$
declare target public.boards%rowtype; baseline_id uuid; next_version integer; task_snapshot jsonb;
begin
  if (select auth.uid()) is null or not public.is_manager() then raise exception 'Somente gestores podem capturar baselines'; end if;
  perform pg_advisory_xact_lock(hashtextextended('baseline:'||p_board_id::text,0));
  select * into target from public.boards where id=p_board_id and status<>'archived' for update;
  if target.id is null then raise exception 'Projeto não encontrado ou arquivado'; end if;
  select coalesce(max(version),0)+1 into next_version from public.project_baselines where board_id=p_board_id;
  select coalesce(jsonb_agg(jsonb_build_object(
    'id',t.id,'title',t.title,'status',t.status,'priority',t.priority,'startDate',t.start_date,
    'dueDate',t.due_date,'assignedTo',t.assigned_to,'estimatedMinutes',t.estimated_minutes,
    'plannedValue',t.planned_value,'isBlocked',t.is_blocked) order by t.position_index,t.created_at),'[]'::jsonb)
    into task_snapshot from public.tasks t where t.board_id=p_board_id and t.archived_at is null;
  update public.project_baselines set is_active=false where board_id=p_board_id and is_active;
  insert into public.project_baselines(board_id,version,name,planned_start,planned_end,budget_amount,snapshot,is_active,created_by,created_at)
  values(p_board_id,next_version,coalesce(nullif(trim(p_name),''),'Baseline '||next_version),target.planned_start,target.planned_end,
    target.budget_amount,jsonb_build_object('tasks',task_snapshot,'taskCount',jsonb_array_length(task_snapshot),
      'estimatedMinutes',(select coalesce(sum(greatest(t.estimated_minutes,0)),0) from public.tasks t where t.board_id=p_board_id and t.archived_at is null),
      'capturedAt',now()),true,(select auth.uid()),now()) returning id into baseline_id;
  update public.boards set baseline_start=planned_start,baseline_end=planned_end where id=p_board_id;
  return baseline_id;
end $$;

create or replace function public.add_portfolio_dependency(
  p_predecessor_board_id uuid,p_successor_board_id uuid,p_dependency_type text default 'finish_to_start',p_lag_days integer default 0)
returns uuid language plpgsql security invoker set search_path='' as $$
declare dependency_id uuid;
begin
  if (select auth.uid()) is null or not public.is_manager() then raise exception 'Somente gestores podem alterar dependências'; end if;
  if p_predecessor_board_id=p_successor_board_id then raise exception 'Um projeto não pode depender dele mesmo'; end if;
  if p_dependency_type not in ('finish_to_start','start_to_start','finish_to_finish','start_to_finish') then
    raise exception 'Tipo de dependência inválido';
  end if;
  perform pg_advisory_xact_lock(hashtextextended('portfolio-dependencies',0));
  if not exists(select 1 from public.boards where id=p_predecessor_board_id and status<>'archived')
    or not exists(select 1 from public.boards where id=p_successor_board_id and status<>'archived') then
    raise exception 'Selecione projetos ativos e acessíveis';
  end if;
  if exists(select 1 from public.portfolio_dependencies where predecessor_board_id=p_predecessor_board_id
    and successor_board_id=p_successor_board_id and predecessor_task_id is null and successor_task_id is null) then
    raise exception 'Esta dependência já existe';
  end if;
  if exists(
    with recursive reachable(id) as (
      select successor_board_id from public.portfolio_dependencies where predecessor_board_id=p_successor_board_id
      union
      select d.successor_board_id from public.portfolio_dependencies d join reachable r on d.predecessor_board_id=r.id
    ) select 1 from reachable where id=p_predecessor_board_id
  ) then raise exception 'A dependência criaria um ciclo entre projetos'; end if;
  insert into public.portfolio_dependencies(predecessor_board_id,successor_board_id,dependency_type,lag_days,created_by,created_at)
  values(p_predecessor_board_id,p_successor_board_id,p_dependency_type,least(365,greatest(-365,p_lag_days)),(select auth.uid()),now())
  returning id into dependency_id;
  return dependency_id;
end $$;

create or replace function private.advance_recurrence(p_value timestamptz,p_cadence text,p_interval integer)
returns timestamptz language sql immutable security invoker set search_path='' as $$
  select case p_cadence when 'daily' then p_value+make_interval(days=>p_interval)
    when 'weekly' then p_value+make_interval(days=>p_interval*7)
    else p_value+make_interval(months=>p_interval) end
$$;

create or replace function private.business_due(p_start timestamptz,p_minutes integer,p_team_id uuid)
returns timestamptz language plpgsql stable security invoker set search_path='' as $$
declare cur timestamptz:=greatest(p_start,date_trunc('day',p_start)+interval '9 hours');remain integer:=greatest(p_minutes,0);part integer;
begin
  while remain>0 loop
    if extract(isodow from cur)>=6 or exists(select 1 from public.company_holidays holiday
      where holiday.holiday_date=cur::date and (holiday.team_id is null or holiday.team_id=p_team_id)) then
      cur=date_trunc('day',cur)+interval '1 day 9 hours';continue;
    end if;
    if cur::time>=time '18:00' then cur=date_trunc('day',cur)+interval '1 day 9 hours';continue;end if;
    part:=least(remain,greatest(0,floor(extract(epoch from ((date_trunc('day',cur)+interval '18 hours')-cur))/60)::integer));
    cur:=cur+make_interval(mins=>part);remain:=remain-part;
  end loop;
  return cur;
end $$;

create or replace function private.set_task_sla()
returns trigger language plpgsql security definer set search_path='' as $$
declare owner_team uuid;
begin
  select profile.team_id into owner_team from public.boards board left join public.profiles profile on profile.id=board.owner_id where board.id=new.board_id;
  if new.sla_minutes is null or new.sla_minutes<=0 then new.sla_due_at=null;new.sla_level=null;
  elsif tg_op='INSERT' or new.sla_minutes is distinct from old.sla_minutes then
    new.sla_due_at=private.business_due(now(),new.sla_minutes,owner_team);new.sla_level='normal';
  end if;
  return new;
end $$;

create or replace function private.generate_recurring_tasks()
returns void language plpgsql security definer set search_path='' as $$
declare r public.recurring_task_rules%rowtype; scheduled_at timestamptz; following_at timestamptz;
  selected_status text; inserted_count integer; local_start date;
begin
  for r in
    select rule.* from public.recurring_task_rules rule join public.boards board on board.id=rule.board_id
    where rule.is_active and board.status<>'archived' and rule.next_run_at<=now()
    order by rule.next_run_at,rule.id for update of rule skip locked
  loop
    if (r.ends_at is not null and r.next_run_at>r.ends_at)
      or (r.max_occurrences is not null and r.occurrences_created>=r.max_occurrences) then
      update public.recurring_task_rules set is_active=false,updated_at=now() where id=r.id;
      continue;
    end if;
    scheduled_at:=r.next_run_at;
    following_at:=private.advance_recurrence(scheduled_at,r.cadence,r.interval_count);
    while following_at<=now() loop
      scheduled_at:=following_at;
      following_at:=private.advance_recurrence(scheduled_at,r.cadence,r.interval_count);
    end loop;
    select case when exists(select 1 from public.boards b, lateral jsonb_array_elements(coalesce(b.settings,'[]'::jsonb)) c
      where b.id=r.board_id and c.value->>'id'=r.target_status) then r.target_status else
      (select c.value->>'id' from public.boards b, lateral jsonb_array_elements(coalesce(b.settings,'[]'::jsonb)) with ordinality c(value,position)
        where b.id=r.board_id and c.value->>'id'<>'done' order by position limit 1) end into selected_status;
    if selected_status is null then
      update public.recurring_task_rules set is_active=false,updated_at=now() where id=r.id;
      continue;
    end if;
    local_start:=(scheduled_at at time zone r.time_zone)::date;
    insert into public.tasks(id,board_id,title,description,status,priority,start_date,due_date,assigned_to,accountable_owner_id,
      created_by,workflow_state,estimated_minutes,custom_fields,recurring_rule_id,recurrence_scheduled_at,position_index,created_at,updated_at)
    values(extensions.uuid_generate_v4(),r.board_id,r.title,r.description,selected_status,r.priority,local_start,local_start+r.due_after_days,
      r.assigned_to,coalesce(r.assigned_to,r.created_by),r.created_by,case when r.assigned_to is null then 'waiting_external' else 'inbox' end,
      greatest(r.estimated_minutes,0),r.custom_fields,r.id,scheduled_at,
      (select coalesce(max(t.position_index),-1)+1 from public.tasks t where t.board_id=r.board_id and t.status=selected_status and t.archived_at is null),now(),now())
    on conflict(recurring_rule_id,recurrence_scheduled_at) where recurring_rule_id is not null do nothing;
    get diagnostics inserted_count=row_count;
    update public.recurring_task_rules set last_run_at=case when inserted_count=1 then now() else last_run_at end,
      occurrences_created=occurrences_created+inserted_count,next_run_at=following_at,updated_at=now(),
      is_active=not ((ends_at is not null and following_at>ends_at)
        or (max_occurrences is not null and occurrences_created+inserted_count>=max_occurrences)) where id=r.id;
  end loop;
end $$;

revoke execute on function public.capture_project_baseline(uuid,text),
  public.add_portfolio_dependency(uuid,uuid,text,integer) from public,anon;
grant execute on function public.capture_project_baseline(uuid,text),
  public.add_portfolio_dependency(uuid,uuid,text,integer) to authenticated;
revoke execute on function private.advance_recurrence(timestamptz,text,integer),private.business_due(timestamptz,integer,uuid),
  private.set_task_sla(),private.generate_recurring_tasks()
  from public,anon,authenticated,service_role;

drop policy if exists templates_read on public.task_templates;
create policy templates_read on public.task_templates for select to authenticated using(
  is_active or created_by=(select auth.uid()) or (select public.is_manager()));

commit;
