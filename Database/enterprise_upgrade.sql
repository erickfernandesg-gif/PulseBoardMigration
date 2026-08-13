-- PulseBoard Enterprise upgrade (blocos 1 a 9)
-- Idempotente: pode ser executado novamente no SQL Editor do Supabase.

create schema if not exists private;

-- 1. Preservacao de historico e campos de governanca
alter table public.profiles add column if not exists is_active boolean not null default true;
alter table public.profiles add column if not exists deactivated_at timestamptz;
alter table public.profiles add column if not exists deactivated_by uuid references public.profiles(id) on delete set null;

alter table public.boards add column if not exists baseline_start date;
alter table public.boards add column if not exists baseline_end date;
alter table public.boards add column if not exists forecast_end date;
alter table public.boards add column if not exists revenue_budget numeric(14,2);
alter table public.boards add column if not exists budget_warning_percent integer not null default 80;

alter table public.tasks add column if not exists parent_task_id uuid references public.tasks(id) on delete restrict;
alter table public.tasks add column if not exists custom_fields jsonb not null default '{}'::jsonb;
alter table public.tasks add column if not exists sla_minutes integer;
alter table public.tasks add column if not exists baseline_start timestamptz;
alter table public.tasks add column if not exists baseline_end timestamptz;
alter table public.tasks add column if not exists planned_value numeric(14,2);
alter table public.tasks add column if not exists archived_at timestamptz;
alter table public.tasks add column if not exists status_updated_at timestamptz not null default now();

create index if not exists profiles_team_active_idx on public.profiles(team_id,is_active);
create index if not exists tasks_parent_idx on public.tasks(parent_task_id) where parent_task_id is not null;
create index if not exists tasks_assignee_open_due_idx on public.tasks(assigned_to,due_date) where status<>'done' and archived_at is null;
create index if not exists tasks_board_status_updated_idx on public.tasks(board_id,status,status_updated_at);
create index if not exists tasks_custom_fields_gin on public.tasks using gin(custom_fields);

-- Impede auto-promocao e alteracao de equipe/estado pelo proprio usuario.
create or replace function public.protect_profile_privileged_fields()
returns trigger language plpgsql security invoker set search_path=public as $$
begin
  if (select auth.uid()) is not null and (select auth.uid())=old.id and not public.is_manager() then
    if new.role is distinct from old.role or new.team_id is distinct from old.team_id
       or new.is_active is distinct from old.is_active
       or new.deactivated_at is distinct from old.deactivated_at
       or new.deactivated_by is distinct from old.deactivated_by then
      raise exception 'Campos administrativos do perfil nao podem ser alterados pelo usuario.';
    end if;
  end if;
  return new;
end $$;
drop trigger if exists protect_profile_privileged_fields on public.profiles;
create trigger protect_profile_privileged_fields before update on public.profiles
for each row execute function public.protect_profile_privileged_fields();

create or replace function public.track_task_status_timestamp()
returns trigger language plpgsql security invoker set search_path=public as $$
begin
  if new.status is distinct from old.status or new.workflow_state is distinct from old.workflow_state then
    new.status_updated_at=now();
  end if;
  return new;
end $$;
drop trigger if exists track_task_status_timestamp on public.tasks;
create trigger track_task_status_timestamp before update on public.tasks
for each row execute function public.track_task_status_timestamp();

-- 6. Capacidade real: feriados, ferias, afastamentos e indisponibilidades
create table if not exists public.company_holidays(
  id uuid primary key default extensions.uuid_generate_v4(),
  holiday_date date not null,
  name text not null,
  team_id uuid references public.teams(id) on delete cascade,
  created_by uuid references public.profiles(id) on delete set null,
  created_at timestamptz not null default now()
);
create unique index if not exists company_holidays_scope_uq
  on public.company_holidays(holiday_date,coalesce(team_id,'00000000-0000-0000-0000-000000000000'::uuid));

create table if not exists public.user_absences(
  id uuid primary key default extensions.uuid_generate_v4(),
  user_id uuid not null references public.profiles(id) on delete restrict,
  absence_type text not null check(absence_type in ('vacation','leave','training','day_off','other')),
  starts_on date not null,
  ends_on date not null,
  notes text,
  status text not null default 'approved' check(status in ('pending','approved','rejected','cancelled')),
  created_by uuid references public.profiles(id) on delete set null,
  created_at timestamptz not null default now(),
  check(ends_on>=starts_on)
);
create index if not exists user_absences_user_range_idx on public.user_absences(user_id,starts_on,ends_on);

-- 7. Baselines e dependencias de portfolio entre projetos
create table if not exists public.project_baselines(
  id uuid primary key default extensions.uuid_generate_v4(),
  board_id uuid not null references public.boards(id) on delete cascade,
  version integer not null,
  name text not null,
  planned_start date,
  planned_end date,
  budget_amount numeric(14,2),
  snapshot jsonb not null default '{}'::jsonb,
  created_by uuid references public.profiles(id) on delete set null,
  created_at timestamptz not null default now(),
  unique(board_id,version)
);
create index if not exists project_baselines_board_idx on public.project_baselines(board_id,version desc);

create table if not exists public.portfolio_dependencies(
  id uuid primary key default extensions.uuid_generate_v4(),
  predecessor_board_id uuid not null references public.boards(id) on delete cascade,
  successor_board_id uuid not null references public.boards(id) on delete cascade,
  predecessor_task_id uuid references public.tasks(id) on delete set null,
  successor_task_id uuid references public.tasks(id) on delete set null,
  dependency_type text not null default 'finish_to_start'
    check(dependency_type in ('finish_to_start','start_to_start','finish_to_finish','start_to_finish')),
  lag_days integer not null default 0,
  created_by uuid references public.profiles(id) on delete set null,
  created_at timestamptz not null default now(),
  check(predecessor_board_id<>successor_board_id),
  unique(predecessor_board_id,successor_board_id,predecessor_task_id,successor_task_id)
);
create index if not exists portfolio_dependencies_predecessor_idx on public.portfolio_dependencies(predecessor_board_id);
create index if not exists portfolio_dependencies_successor_idx on public.portfolio_dependencies(successor_board_id);

-- 8. Modelos, recorrencia e campos avancados
create table if not exists public.task_templates(
  id uuid primary key default extensions.uuid_generate_v4(),
  name text not null,
  description text,
  board_id uuid references public.boards(id) on delete cascade,
  team_id uuid references public.teams(id) on delete cascade,
  definition jsonb not null default '{}'::jsonb,
  is_active boolean not null default true,
  created_by uuid references public.profiles(id) on delete set null,
  created_at timestamptz not null default now()
);

create table if not exists public.recurring_task_rules(
  id uuid primary key default extensions.uuid_generate_v4(),
  board_id uuid not null references public.boards(id) on delete cascade,
  template_id uuid references public.task_templates(id) on delete set null,
  title text not null,
  description text,
  cadence text not null check(cadence in ('daily','weekly','monthly')),
  interval_count integer not null default 1 check(interval_count between 1 and 365),
  next_run_at timestamptz not null,
  assigned_to uuid references public.profiles(id) on delete set null,
  estimated_minutes integer not null default 0,
  due_after_days integer not null default 0,
  is_active boolean not null default true,
  custom_fields jsonb not null default '{}'::jsonb,
  created_by uuid references public.profiles(id) on delete set null,
  created_at timestamptz not null default now(),
  last_run_at timestamptz
);
create index if not exists recurring_task_rules_due_idx on public.recurring_task_rules(next_run_at) where is_active;

-- 9. Mencoes, respostas, arquivos gerais e busca
create table if not exists public.task_mentions(
  id uuid primary key default extensions.uuid_generate_v4(),
  task_id uuid not null references public.tasks(id) on delete cascade,
  comment_id uuid not null references public.task_comments(id) on delete cascade,
  mentioned_user_id uuid not null references public.profiles(id) on delete cascade,
  mentioned_by uuid not null references public.profiles(id) on delete restrict,
  created_at timestamptz not null default now(),
  unique(comment_id,mentioned_user_id)
);
create index if not exists task_mentions_user_idx on public.task_mentions(mentioned_user_id,created_at desc);

create table if not exists public.task_files(
  id uuid primary key default extensions.uuid_generate_v4(),
  task_id uuid not null references public.tasks(id) on delete cascade,
  uploaded_by uuid not null references public.profiles(id) on delete restrict,
  storage_path text not null unique,
  file_name text not null,
  content_type text not null,
  file_size bigint not null check(file_size>0),
  description text,
  version integer not null default 1,
  previous_version_id uuid references public.task_files(id) on delete set null,
  created_at timestamptz not null default now()
);
create index if not exists task_files_task_idx on public.task_files(task_id,created_at desc);

alter table public.tasks add column if not exists search_vector tsvector
  generated always as (
    setweight(to_tsvector('portuguese',coalesce(title,'')),'A') ||
    setweight(to_tsvector('portuguese',coalesce(description,'')),'B')
  ) stored;
create index if not exists tasks_search_vector_idx on public.tasks using gin(search_vector);

alter table public.task_comments add column if not exists search_vector tsvector
  generated always as (to_tsvector('portuguese',coalesce(content,''))) stored;
create index if not exists task_comments_search_vector_idx on public.task_comments using gin(search_vector);

-- Preferencias de alertas
create table if not exists public.notification_preferences(
  user_id uuid primary key references public.profiles(id) on delete cascade,
  in_app boolean not null default true,
  email_digest boolean not null default false,
  due_reminders boolean not null default true,
  budget_alerts boolean not null default true,
  mention_alerts boolean not null default true,
  digest_hour smallint not null default 8 check(digest_hour between 0 and 23),
  updated_at timestamptz not null default now()
);

-- RLS nas novas entidades
alter table public.company_holidays enable row level security;
alter table public.user_absences enable row level security;
alter table public.project_baselines enable row level security;
alter table public.portfolio_dependencies enable row level security;
alter table public.task_templates enable row level security;
alter table public.recurring_task_rules enable row level security;
alter table public.task_mentions enable row level security;
alter table public.task_files enable row level security;
alter table public.notification_preferences enable row level security;

-- Remove politicas concorrentes apenas das tabelas que esta migracao consolida.
do $$ declare item record;
begin
  for item in select schemaname,tablename,policyname from pg_policies
    where schemaname='public' and tablename in (
      'profiles','task_collaborators','task_checklists','time_logs','user_rates',
      'company_holidays','user_absences','project_baselines','portfolio_dependencies',
      'task_templates','recurring_task_rules','task_mentions','task_files','notification_preferences')
  loop execute format('drop policy if exists %I on %I.%I',item.policyname,item.schemaname,item.tablename); end loop;
end $$;

create policy profiles_read on public.profiles for select to authenticated using(true);
create policy profiles_update on public.profiles for update to authenticated
  using(id=(select auth.uid()) or public.is_manager())
  with check(id=(select auth.uid()) or public.is_manager());

create policy collaborators_select on public.task_collaborators for select to authenticated using(
  exists(select 1 from public.tasks t where t.id=task_collaborators.task_id));
create policy collaborators_write on public.task_collaborators for all to authenticated
  using(exists(select 1 from public.tasks t where t.id=task_collaborators.task_id and
    (t.assigned_to=(select auth.uid()) or t.accountable_owner_id=(select auth.uid()) or public.is_manager()
     or exists(select 1 from public.boards b where b.id=t.board_id and b.owner_id=(select auth.uid())))))
  with check(exists(select 1 from public.tasks t where t.id=task_collaborators.task_id and
    (t.assigned_to=(select auth.uid()) or t.accountable_owner_id=(select auth.uid()) or public.is_manager()
     or exists(select 1 from public.boards b where b.id=t.board_id and b.owner_id=(select auth.uid())))));

create policy checklists_select on public.task_checklists for select to authenticated using(
  exists(select 1 from public.tasks t where t.id=task_checklists.task_id));
create policy checklists_write on public.task_checklists for all to authenticated
  using(exists(select 1 from public.tasks t where t.id=task_checklists.task_id and
    (t.assigned_to=(select auth.uid()) or t.accountable_owner_id=(select auth.uid()) or public.is_manager())))
  with check(exists(select 1 from public.tasks t where t.id=task_checklists.task_id and
    (t.assigned_to=(select auth.uid()) or t.accountable_owner_id=(select auth.uid()) or public.is_manager())));

create policy rates_read on public.user_rates for select to authenticated using(
  user_id=(select auth.uid()) or public.can_manage_user(user_id));
create policy rates_write on public.user_rates for all to authenticated
  using(public.can_manage_user(user_id)) with check(public.can_manage_user(user_id));

create policy time_logs_read on public.time_logs for select to authenticated using(
  user_id=(select auth.uid()) or public.can_manage_user(user_id));
create policy time_logs_insert on public.time_logs for insert to authenticated with check(
  user_id=(select auth.uid()) and exists(select 1 from public.tasks t where t.id=time_logs.task_id));
create policy time_logs_update on public.time_logs for update to authenticated
  using((user_id=(select auth.uid()) and approval_status='pending' and billing_status='unbilled') or public.can_manage_user(user_id))
  with check((user_id=(select auth.uid()) and billing_status='unbilled') or public.can_manage_user(user_id));
create policy time_logs_delete on public.time_logs for delete to authenticated using(
  ((user_id=(select auth.uid()) and approval_status='pending') or public.can_manage_user(user_id)) and billing_status='unbilled');

create policy holidays_read on public.company_holidays for select to authenticated using(true);
create policy holidays_manage on public.company_holidays for all to authenticated
  using(public.is_manager()) with check(public.is_manager());
create policy absences_read on public.user_absences for select to authenticated using(
  user_id=(select auth.uid()) or public.can_manage_user(user_id));
create policy absences_write on public.user_absences for all to authenticated
  using(user_id=(select auth.uid()) or public.can_manage_user(user_id))
  with check(user_id=(select auth.uid()) or public.can_manage_user(user_id));

create policy baselines_read on public.project_baselines for select to authenticated using(
  exists(select 1 from public.boards b where b.id=project_baselines.board_id));
create policy baselines_manage on public.project_baselines for all to authenticated
  using(public.is_manager() or created_by=(select auth.uid()))
  with check(public.is_manager() or created_by=(select auth.uid()));
create policy portfolio_dependencies_read on public.portfolio_dependencies for select to authenticated using(
  exists(select 1 from public.boards b where b.id=predecessor_board_id)
  and exists(select 1 from public.boards b where b.id=successor_board_id));
create policy portfolio_dependencies_manage on public.portfolio_dependencies for all to authenticated
  using(public.is_manager()) with check(public.is_manager());

create policy templates_read on public.task_templates for select to authenticated using(is_active);
create policy templates_manage on public.task_templates for all to authenticated
  using(public.is_manager() or created_by=(select auth.uid()))
  with check(public.is_manager() or created_by=(select auth.uid()));
create policy recurring_read on public.recurring_task_rules for select to authenticated using(
  exists(select 1 from public.boards b where b.id=recurring_task_rules.board_id));
create policy recurring_manage on public.recurring_task_rules for all to authenticated
  using(public.is_manager() or created_by=(select auth.uid()))
  with check(public.is_manager() or created_by=(select auth.uid()));

create policy mentions_read on public.task_mentions for select to authenticated using(
  mentioned_user_id=(select auth.uid()) or exists(select 1 from public.tasks t where t.id=task_mentions.task_id));
create policy mentions_insert on public.task_mentions for insert to authenticated with check(
  mentioned_by=(select auth.uid()) and exists(select 1 from public.tasks t where t.id=task_mentions.task_id));
create policy files_read on public.task_files for select to authenticated using(
  exists(select 1 from public.tasks t where t.id=task_files.task_id));
create policy files_write on public.task_files for all to authenticated
  using(uploaded_by=(select auth.uid()) or public.is_manager())
  with check(uploaded_by=(select auth.uid()) and exists(select 1 from public.tasks t where t.id=task_files.task_id));
create policy preferences_own on public.notification_preferences for all to authenticated
  using(user_id=(select auth.uid())) with check(user_id=(select auth.uid()));

-- Notificacao de mencoes
create or replace function public.notify_task_mention()
returns trigger language plpgsql security definer set search_path=public as $$
declare target public.tasks%rowtype;
begin
  select * into target from public.tasks where id=new.task_id;
  insert into public.notifications(recipient_id,actor_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
  values(new.mentioned_user_id,new.mentioned_by,new.task_id,target.board_id,'mention','Voce foi mencionado',target.title,
    '/Boards/Details/'||target.board_id::text,'high','mention:'||new.id::text)
  on conflict(deduplication_key) do nothing;
  return new;
end $$;
drop trigger if exists notify_task_mention on public.task_mentions;
create trigger notify_task_mention after insert on public.task_mentions for each row execute function public.notify_task_mention();

-- Busca global com ranking; as consultas internas continuam submetidas ao RLS.
create or replace function public.search_workspace(search_query text, result_limit integer default 30)
returns table(kind text,id uuid,board_id uuid,title text,snippet text,action_url text,rank real)
language sql stable security invoker set search_path=public as $$
  with q as (select websearch_to_tsquery('portuguese',left(trim(search_query),200)) value), results as (
    select 'task'::text,t.id,t.board_id,t.title,left(coalesce(t.description,''),220),
      '/Boards/Details/'||t.board_id::text,ts_rank(t.search_vector,q.value)::real rank
    from public.tasks t,q where t.archived_at is null and t.search_vector@@q.value
    union all
    select 'comment',c.id,t.board_id,t.title,left(c.content,220),
      '/Boards/Details/'||t.board_id::text,ts_rank(c.search_vector,q.value)::real
    from public.task_comments c join public.tasks t on t.id=c.task_id,q
    where c.deleted_at is null and c.search_vector@@q.value
    union all
    select 'file',f.id,t.board_id,f.file_name,left(coalesce(f.description,''),220),
      '/Boards/Details/'||t.board_id::text,0.25::real
    from public.task_files f join public.tasks t on t.id=f.task_id
    where f.file_name ilike '%'||replace(left(trim(search_query),100),'%','')||'%'
  ) select * from results order by rank desc,title limit greatest(1,least(result_limit,100));
$$;

-- Faturamento atomico e idempotente. Uma chamada RPC inteira e uma transacao PostgreSQL.
create or replace function public.generate_billing_invoice(
  p_client_id uuid,p_creator_id uuid,p_period_start date,p_period_end date,p_due_date date default null)
returns uuid language plpgsql security invoker set search_path=public as $$
declare v_invoice_id uuid; v_reference text; v_total numeric(14,2); v_contract_id uuid;
begin
  if not public.is_manager() then raise exception 'Acesso financeiro negado.'; end if;
  if p_period_end<p_period_start then raise exception 'Periodo de faturamento invalido.'; end if;
  perform pg_advisory_xact_lock(hashtext('invoice:'||p_client_id::text||':'||p_period_start::text||':'||p_period_end::text));
  select c.id into v_contract_id from public.client_contracts c
    where c.client_id=p_client_id and c.is_active and c.starts_on<=p_period_end
      and (c.ends_on is null or c.ends_on>=p_period_start)
    order by (c.board_id is not null) desc,c.starts_on desc limit 1;
  with locked_logs as materialized(
    select l.id,l.minutes,l.billing_rate_snapshot
    from public.time_logs l join public.tasks t on t.id=l.task_id
    where t.client_id=p_client_id and l.is_billable and l.approval_status='approved'
      and l.billing_status='unbilled' and l.log_date between p_period_start and p_period_end
    for update of l)
  select coalesce(sum(billing_rate_snapshot*minutes/60.0),0) into v_total from locked_logs;
  if v_total<=0 then raise exception 'Nao existem horas aprovadas e nao faturadas nesse periodo.'; end if;
  v_invoice_id=extensions.uuid_generate_v4();
  v_reference='PB-'||to_char(now(),'YYYYMMDD')||'-'||upper(substr(replace(v_invoice_id::text,'-',''),1,6));
  insert into public.billing_invoices(id,client_id,contract_id,reference,status,period_start,period_end,due_date,subtotal,total,created_by)
    values(v_invoice_id,p_client_id,v_contract_id,v_reference,'draft',p_period_start,p_period_end,p_due_date,v_total,v_total,p_creator_id);
  insert into public.billing_invoice_items(invoice_id,time_log_id,description,minutes,unit_rate,amount)
    select v_invoice_id,l.id,t.title||' - '||to_char(l.log_date,'DD/MM/YYYY'),l.minutes,l.billing_rate_snapshot,
      l.billing_rate_snapshot*l.minutes/60.0
    from public.time_logs l join public.tasks t on t.id=l.task_id
    where t.client_id=p_client_id and l.is_billable and l.approval_status='approved'
      and l.billing_status='unbilled' and l.log_date between p_period_start and p_period_end;
  update public.time_logs l set billing_status='invoiced',invoice_id=v_invoice_id
    from public.tasks t where t.id=l.task_id and t.client_id=p_client_id and l.is_billable
      and l.approval_status='approved' and l.billing_status='unbilled'
      and l.log_date between p_period_start and p_period_end;
  return v_invoice_id;
end $$;

-- Jobs internos: alertas e recorrencias. Funcoes privadas nao ficam expostas pela Data API.
create or replace function private.generate_operational_notifications()
returns void language plpgsql security definer set search_path='' as $$
begin
  insert into public.notifications(recipient_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
  select t.assigned_to,t.id,t.board_id,
    case when t.due_date<now() then 'task_overdue' else 'task_due_soon' end,
    case when t.due_date<now() then 'Tarefa atrasada' else 'Prazo proximo' end,t.title,
    '/Boards/Details/'||t.board_id::text,case when t.due_date<now() then 'critical' else 'high' end,
    (case when t.due_date<now() then 'overdue:' else 'due:' end)||t.id::text||':'||current_date::text
  from public.tasks t left join public.notification_preferences p on p.user_id=t.assigned_to
  where t.assigned_to is not null and t.status<>'done' and t.archived_at is null and t.due_date<now()+interval '24 hours'
    and coalesce(p.due_reminders,true)
  on conflict(deduplication_key) do nothing;

  insert into public.notifications(recipient_id,board_id,type,title,message,action_url,priority,deduplication_key)
  select b.owner_id,b.id,'budget_threshold','Alerta de orcamento',b.name||' atingiu '||round(x.spent/nullif(b.budget_amount,0)*100)||'% do orcamento',
    '/Executive','critical','budget:'||b.id::text||':'||floor(x.spent/nullif(b.budget_amount,0)*10)::text
  from public.boards b cross join lateral(
    select coalesce(sum(l.cost_rate_snapshot*l.minutes/60.0),0) spent
    from public.tasks t join public.time_logs l on l.task_id=t.id where t.board_id=b.id) x
  left join public.notification_preferences p on p.user_id=b.owner_id
  where b.budget_amount>0 and x.spent>=b.budget_amount*b.budget_warning_percent/100.0 and coalesce(p.budget_alerts,true)
  on conflict(deduplication_key) do nothing;
end $$;

create or replace function private.generate_recurring_tasks()
returns void language plpgsql security definer set search_path='' as $$
declare r public.recurring_task_rules%rowtype; new_id uuid;
begin
  for r in select * from public.recurring_task_rules where is_active and next_run_at<=now() for update skip locked loop
    new_id=extensions.uuid_generate_v4();
    insert into public.tasks(id,board_id,title,description,status,priority,start_date,due_date,assigned_to,accountable_owner_id,
      created_by,workflow_state,estimated_minutes,custom_fields,created_at,updated_at)
    values(new_id,r.board_id,r.title,r.description,'todo','medium',current_date,current_date+r.due_after_days,r.assigned_to,
      coalesce(r.assigned_to,r.created_by),r.created_by,case when r.assigned_to is null then 'waiting_external' else 'inbox' end,
      r.estimated_minutes,r.custom_fields,now(),now());
    update public.recurring_task_rules set last_run_at=now(),next_run_at=case cadence
      when 'daily' then next_run_at+(interval_count||' days')::interval
      when 'weekly' then next_run_at+(interval_count*7||' days')::interval
      else next_run_at+(interval_count||' months')::interval end where id=r.id;
  end loop;
end $$;

revoke all on all functions in schema private from public,anon,authenticated;
revoke execute on function public.notify_task_mention() from public,anon,authenticated;
revoke execute on function public.search_workspace(text,integer) from public,anon;
revoke execute on function public.generate_billing_invoice(uuid,uuid,date,date,date) from public,anon;
grant execute on function public.search_workspace(text,integer) to authenticated;
grant execute on function public.generate_billing_invoice(uuid,uuid,date,date,date) to authenticated;

grant select,insert,update on public.profiles to authenticated;
grant select,insert,update,delete on public.company_holidays,public.user_absences,public.project_baselines,
  public.portfolio_dependencies,public.task_templates,public.recurring_task_rules,public.task_mentions,
  public.task_files,public.notification_preferences to authenticated;

-- Agenda jobs se pg_cron estiver disponivel. O nome evita duplicidade.
do $$
begin
  if exists(select 1 from pg_extension where extname='pg_cron') then
    if not exists(select 1 from cron.job where jobname='pulseboard-operational-alerts') then
      perform cron.schedule('pulseboard-operational-alerts','*/10 * * * *','select private.generate_operational_notifications()');
    end if;
    if not exists(select 1 from cron.job where jobname='pulseboard-recurring-tasks') then
      perform cron.schedule('pulseboard-recurring-tasks','*/15 * * * *','select private.generate_recurring_tasks()');
    end if;
  end if;
end $$;
