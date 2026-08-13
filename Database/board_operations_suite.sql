-- Board Operations Suite. Idempotente.
alter table public.automations add column if not exists board_id uuid references public.boards(id) on delete cascade;
alter table public.automations add column if not exists condition_field text;
alter table public.tasks add column if not exists sla_due_at timestamptz;
alter table public.tasks add column if not exists sla_level text;

create table if not exists public.task_field_history(id uuid primary key default gen_random_uuid(),task_id uuid not null references public.tasks(id) on delete cascade,board_id uuid not null references public.boards(id) on delete cascade,changed_by uuid references public.profiles(id) on delete set null,field_name text not null,old_value jsonb,new_value jsonb,created_at timestamptz not null default now());
create table if not exists public.intake_forms(id uuid primary key default gen_random_uuid(),board_id uuid not null references public.boards(id) on delete cascade,title text not null check(length(title) between 1 and 200),description text,public_token text not null unique check(length(public_token)=48),target_status text not null default 'backlog',default_priority text not null default 'medium' check(default_priority in('low','medium','high','critical')),require_email boolean not null default true,is_active boolean not null default true,created_by uuid not null references public.profiles(id),created_at timestamptz not null default now());
create table if not exists public.task_approval_steps(id uuid primary key default gen_random_uuid(),task_id uuid not null references public.tasks(id) on delete cascade,sequence integer not null check(sequence>0),approver_id uuid not null references public.profiles(id),status text not null default 'waiting' check(status in('waiting','pending','approved','rejected','cancelled')),decision_by uuid references public.profiles(id),decision_note text,decided_at timestamptz,created_at timestamptz not null default now(),unique(task_id,sequence));
create table if not exists public.approval_delegations(id uuid primary key default gen_random_uuid(),delegator_id uuid not null references public.profiles(id),substitute_id uuid not null references public.profiles(id),starts_on date not null,ends_on date not null,is_active boolean not null default true,created_by uuid not null references public.profiles(id),created_at timestamptz not null default now(),check(delegator_id<>substitute_id and ends_on>=starts_on));
create table if not exists public.task_field_mirrors(id uuid primary key default gen_random_uuid(),source_task_id uuid not null references public.tasks(id) on delete cascade,target_task_id uuid not null references public.tasks(id) on delete cascade,field_name text not null check(field_name in('status','priority','due_date','assigned_to')),is_active boolean not null default true,created_by uuid not null references public.profiles(id),created_at timestamptz not null default now(),check(source_task_id<>target_task_id),unique(source_task_id,target_task_id,field_name));

create index if not exists field_history_task_idx on public.task_field_history(task_id,created_at desc);
create index if not exists field_history_board_idx on public.task_field_history(board_id,created_at desc);
create index if not exists intake_forms_board_idx on public.intake_forms(board_id);
create index if not exists approval_steps_task_idx on public.task_approval_steps(task_id,sequence);
create index if not exists approval_steps_pending_idx on public.task_approval_steps(approver_id) where status='pending';
create index if not exists approval_delegations_idx on public.approval_delegations(delegator_id,starts_on,ends_on) where is_active;
create index if not exists mirrors_source_idx on public.task_field_mirrors(source_task_id) where is_active;
create index if not exists mirrors_target_idx on public.task_field_mirrors(target_task_id) where is_active;
create index if not exists field_history_changed_by_idx on public.task_field_history(changed_by) where changed_by is not null;
create index if not exists intake_forms_created_by_idx on public.intake_forms(created_by);
create index if not exists approval_steps_approver_idx on public.task_approval_steps(approver_id);
create index if not exists approval_steps_decision_by_idx on public.task_approval_steps(decision_by) where decision_by is not null;
create index if not exists approval_delegations_substitute_idx on public.approval_delegations(substitute_id);
create index if not exists approval_delegations_created_by_idx on public.approval_delegations(created_by);
create index if not exists mirrors_created_by_idx on public.task_field_mirrors(created_by);
create index if not exists automations_board_idx on public.automations(board_id) where is_active;
create index if not exists tasks_sla_idx on public.tasks(sla_due_at) where archived_at is null and status<>'done' and sla_due_at is not null;

alter table public.task_field_history enable row level security;
alter table public.intake_forms enable row level security;
alter table public.task_approval_steps enable row level security;
alter table public.approval_delegations enable row level security;
alter table public.task_field_mirrors enable row level security;
drop policy if exists field_history_read on public.task_field_history;
create policy field_history_read on public.task_field_history for select to authenticated using(private.can_read_task(task_id));
drop policy if exists intake_forms_manage on public.intake_forms;
drop policy if exists intake_forms_insert on public.intake_forms;drop policy if exists intake_forms_update on public.intake_forms;drop policy if exists intake_forms_delete on public.intake_forms;
create policy intake_forms_manage on public.intake_forms for select to authenticated using(private.can_edit_board(board_id));
create policy intake_forms_insert on public.intake_forms for insert to authenticated with check(private.can_edit_board(board_id));
create policy intake_forms_update on public.intake_forms for update to authenticated using(private.can_edit_board(board_id)) with check(private.can_edit_board(board_id));
create policy intake_forms_delete on public.intake_forms for delete to authenticated using(private.can_edit_board(board_id));
drop policy if exists approval_steps_read on public.task_approval_steps;
create policy approval_steps_read on public.task_approval_steps for select to authenticated using(private.can_read_task(task_id));
drop policy if exists approval_steps_manage on public.task_approval_steps;
drop policy if exists approval_steps_insert on public.task_approval_steps;drop policy if exists approval_steps_update on public.task_approval_steps;drop policy if exists approval_steps_delete on public.task_approval_steps;
create policy approval_steps_insert on public.task_approval_steps for insert to authenticated with check(private.can_edit_task(task_id));
create policy approval_steps_update on public.task_approval_steps for update to authenticated using(private.can_edit_task(task_id)) with check(private.can_edit_task(task_id));
create policy approval_steps_delete on public.task_approval_steps for delete to authenticated using(private.can_edit_task(task_id));
drop policy if exists approval_delegations_read on public.approval_delegations;
create policy approval_delegations_read on public.approval_delegations for select to authenticated using(delegator_id=(select auth.uid()) or substitute_id=(select auth.uid()) or public.is_manager());
drop policy if exists approval_delegations_manage on public.approval_delegations;
drop policy if exists delegations_insert on public.approval_delegations;drop policy if exists delegations_update on public.approval_delegations;drop policy if exists delegations_delete on public.approval_delegations;
create policy delegations_insert on public.approval_delegations for insert to authenticated with check((delegator_id=(select auth.uid()) or public.is_manager()) and created_by=(select auth.uid()));
create policy delegations_update on public.approval_delegations for update to authenticated using(delegator_id=(select auth.uid()) or public.is_manager()) with check(delegator_id=(select auth.uid()) or public.is_manager());
create policy delegations_delete on public.approval_delegations for delete to authenticated using(delegator_id=(select auth.uid()) or public.is_manager());
drop policy if exists mirrors_read on public.task_field_mirrors;
create policy mirrors_read on public.task_field_mirrors for select to authenticated using(private.can_read_task(source_task_id) or private.can_read_task(target_task_id));
drop policy if exists mirrors_manage on public.task_field_mirrors;
drop policy if exists mirrors_insert on public.task_field_mirrors;drop policy if exists mirrors_update on public.task_field_mirrors;drop policy if exists mirrors_delete on public.task_field_mirrors;
create policy mirrors_insert on public.task_field_mirrors for insert to authenticated with check(private.can_edit_task(source_task_id) and private.can_edit_task(target_task_id));
create policy mirrors_update on public.task_field_mirrors for update to authenticated using(private.can_edit_task(source_task_id) and private.can_edit_task(target_task_id)) with check(private.can_edit_task(source_task_id) and private.can_edit_task(target_task_id));
create policy mirrors_delete on public.task_field_mirrors for delete to authenticated using(private.can_edit_task(source_task_id) and private.can_edit_task(target_task_id));
grant select on public.task_field_history to authenticated;
grant select,insert,update,delete on public.intake_forms,public.task_approval_steps,public.approval_delegations,public.task_field_mirrors to authenticated;
grant select,insert,update,delete on public.intake_forms,public.task_approval_steps,public.approval_delegations,public.task_field_mirrors,public.task_field_history to service_role;

create or replace function private.audit_task_fields() returns trigger language plpgsql security definer set search_path='' as $$
declare f text;o jsonb:=to_jsonb(old);n jsonb:=to_jsonb(new);
begin
 foreach f in array array['title','description','status','priority','start_date','due_date','assigned_to','accountable_owner_id','workflow_state','client_id','target_month','estimated_minutes','sla_minutes','planned_value','is_blocked','blocker_reason','custom_fields','archived_at'] loop
  if o->f is distinct from n->f then insert into public.task_field_history(task_id,board_id,changed_by,field_name,old_value,new_value) values(new.id,new.board_id,(select auth.uid()),f,o->f,n->f);end if;
 end loop;return new;
end $$;
drop trigger if exists audit_task_fields on public.tasks;
create trigger audit_task_fields after update on public.tasks for each row execute function private.audit_task_fields();

create or replace function private.enforce_board_wip() returns trigger language plpgsql security invoker set search_path='' as $$
declare lim integer;used integer;
begin
 if new.archived_at is not null or (tg_op='UPDATE' and new.status is not distinct from old.status) then return new;end if;
 select nullif(c->>'wip_limit','')::integer into lim from public.boards b cross join lateral jsonb_array_elements(coalesce(b.settings,'[]'::jsonb)) c where b.id=new.board_id and c->>'id'=new.status limit 1;
 if lim is null then return new;end if;
 select count(*) into used from public.tasks where board_id=new.board_id and status=new.status and archived_at is null and id<>new.id;
 if used>=lim then raise exception 'Limite WIP da etapa atingido (%).',lim using errcode='P0001';end if;return new;
end $$;
drop trigger if exists enforce_board_wip on public.tasks;
create trigger enforce_board_wip before insert or update of status,archived_at on public.tasks for each row execute function private.enforce_board_wip();

create or replace function public.bulk_manage_tasks(p_board_id uuid,p_task_ids uuid[],p_action text,p_assigned_to uuid default null,p_status text default null,p_due_date date default null,p_priority text default null)
returns integer language plpgsql security invoker set search_path='' as $$
declare affected integer;expected integer;
begin
 if not private.can_edit_board(p_board_id) then raise exception 'Sem permissão.' using errcode='42501';end if;
 expected:=coalesce(array_length(p_task_ids,1),0);if expected=0 or expected>500 then raise exception 'Seleção inválida.';end if;
 if p_action='assign' then update public.tasks set assigned_to=p_assigned_to,workflow_state=case when p_assigned_to is null then 'waiting_external' else 'inbox' end where board_id=p_board_id and id=any(p_task_ids) and archived_at is null;
 elsif p_action='move' then
  if not exists(select 1 from public.boards b cross join lateral jsonb_array_elements(coalesce(b.settings,'[]'::jsonb)) c where b.id=p_board_id and c->>'id'=p_status) then raise exception 'Etapa inválida.';end if;
  update public.tasks set status=p_status,status_updated_at=now() where board_id=p_board_id and id=any(p_task_ids) and archived_at is null;
 elsif p_action='archive' then update public.tasks set archived_at=now() where board_id=p_board_id and id=any(p_task_ids) and archived_at is null;
 elsif p_action='due_date' then update public.tasks set due_date=p_due_date where board_id=p_board_id and id=any(p_task_ids) and archived_at is null;
 elsif p_action='priority' then if p_priority not in('low','medium','high','critical') then raise exception 'Prioridade inválida.';end if;update public.tasks set priority=p_priority where board_id=p_board_id and id=any(p_task_ids) and archived_at is null;
 else raise exception 'Ação inválida.';end if;
 get diagnostics affected=row_count;if affected<>expected then raise exception 'Uma ou mais tarefas são inválidas ou já estão arquivadas.';end if;return affected;
end $$;
revoke execute on function public.bulk_manage_tasks(uuid,uuid[],text,uuid,text,date,text) from public,anon;
grant execute on function public.bulk_manage_tasks(uuid,uuid[],text,uuid,text,date,text) to authenticated;

create or replace function private.mirror_task_fields() returns trigger language plpgsql security definer set search_path='' as $$
declare m record;
begin
 if pg_trigger_depth()>1 then return new;end if;
 for m in select * from public.task_field_mirrors where source_task_id=new.id and is_active loop
  if m.field_name='status' and new.status is distinct from old.status then update public.tasks set status=new.status,status_updated_at=now() where id=m.target_task_id;
  elsif m.field_name='priority' and new.priority is distinct from old.priority then update public.tasks set priority=new.priority where id=m.target_task_id;
  elsif m.field_name='due_date' and new.due_date is distinct from old.due_date then update public.tasks set due_date=new.due_date where id=m.target_task_id;
  elsif m.field_name='assigned_to' and new.assigned_to is distinct from old.assigned_to then update public.tasks set assigned_to=new.assigned_to where id=m.target_task_id;end if;
 end loop;return new;
end $$;
drop trigger if exists mirror_task_fields on public.tasks;
create trigger mirror_task_fields after update of status,priority,due_date,assigned_to on public.tasks for each row execute function private.mirror_task_fields();

create or replace function public.decide_task_approval(p_step_id uuid,p_decision text,p_note text default null) returns void language plpgsql security invoker set search_path='' as $$
declare s public.task_approval_steps;a uuid:=(select auth.uid());effective uuid;nxt uuid;b uuid;
begin
 select * into s from public.task_approval_steps where id=p_step_id for update;
 if s.id is null or s.status<>'pending' or p_decision not in('approve','reject') then raise exception 'Etapa inválida.';end if;
 select coalesce((select d.substitute_id from public.approval_delegations d where d.delegator_id=s.approver_id and d.is_active and current_date between d.starts_on and d.ends_on order by d.created_at desc limit 1),s.approver_id) into effective;
 if a<>effective and not public.is_manager() then raise exception 'Aprovação pertence a outro usuário.' using errcode='42501';end if;
 update public.task_approval_steps set status=case when p_decision='approve' then 'approved' else 'rejected' end,decision_by=a,decision_note=nullif(trim(p_note),''),decided_at=now() where id=p_step_id;
 select board_id into b from public.tasks where id=s.task_id;
 if p_decision='reject' then update public.tasks set workflow_state='changes_requested',is_blocked=true,blocker_reason=coalesce(nullif(trim(p_note),''),'Aprovação rejeitada') where id=s.task_id;
 else select id into nxt from public.task_approval_steps where task_id=s.task_id and sequence>s.sequence and status='waiting' order by sequence limit 1 for update;
  if nxt is not null then update public.task_approval_steps set status='pending' where id=nxt;else update public.tasks set workflow_state=case when status='done' then 'done' else 'accepted' end,is_blocked=false,blocker_reason=null where id=s.task_id;end if;
 end if;
 insert into public.activity_log(task_id,board_id,user_id,action,details) values(s.task_id,b,a,'approval_decided',jsonb_build_object('sequence',s.sequence,'decision',p_decision,'note',p_note));
end $$;
revoke execute on function public.decide_task_approval(uuid,text,text) from public,anon;
grant execute on function public.decide_task_approval(uuid,text,text) to authenticated;

create or replace function private.start_required_approval() returns trigger language plpgsql security definer set search_path='' as $$
declare req boolean;step_id uuid;approver uuid;
begin
 if new.status is not distinct from old.status then return new;end if;
 select coalesce((c->>'requires_approval')::boolean,false) into req from public.boards b cross join lateral jsonb_array_elements(coalesce(b.settings,'[]'::jsonb)) c where b.id=new.board_id and c->>'id'=new.status limit 1;
 if not coalesce(req,false) then return new;end if;
 select id,approver_id into step_id,approver from public.task_approval_steps where task_id=new.id and status in('waiting','pending') order by sequence limit 1;
 if step_id is not null then
  update public.task_approval_steps set status='pending' where id=step_id;update public.tasks set workflow_state='waiting_review' where id=new.id;
  insert into public.notifications(recipient_id,user_id,actor_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key) values(approver,approver,(select auth.uid()),new.id,new.board_id,'approval_required','Aprovação pendente',new.title,'/Boards/Details/'||new.board_id,'high','approval:'||step_id) on conflict(deduplication_key) where deduplication_key is not null do nothing;
 end if;return new;
end $$;
drop trigger if exists start_required_approval on public.tasks;
create trigger start_required_approval after update of status on public.tasks for each row execute function private.start_required_approval();

create or replace function private.business_due(p_start timestamptz,p_minutes integer) returns timestamptz language plpgsql stable security invoker set search_path='' as $$
declare cur timestamptz:=greatest(p_start,date_trunc('day',p_start)+interval '9 hours');remain integer:=greatest(p_minutes,0);part integer;
begin
 while remain>0 loop
  if extract(isodow from cur)>=6 or exists(select 1 from public.company_holidays h where h.holiday_date=cur::date) then cur=date_trunc('day',cur)+interval '1 day 9 hours';continue;end if;
  if cur::time>=time '18:00' then cur=date_trunc('day',cur)+interval '1 day 9 hours';continue;end if;
  part:=least(remain,greatest(0,floor(extract(epoch from ((date_trunc('day',cur)+interval '18 hours')-cur))/60)::integer));cur:=cur+make_interval(mins=>part);remain:=remain-part;
 end loop;return cur;
end $$;
create or replace function private.set_task_sla() returns trigger language plpgsql security definer set search_path='' as $$
begin if new.sla_minutes is null or new.sla_minutes<=0 then new.sla_due_at=null;new.sla_level=null;elsif tg_op='INSERT' or new.sla_minutes is distinct from old.sla_minutes then new.sla_due_at=private.business_due(now(),new.sla_minutes);new.sla_level='normal';end if;return new;end $$;
drop trigger if exists set_task_sla on public.tasks;
create trigger set_task_sla before insert or update of sla_minutes on public.tasks for each row execute function private.set_task_sla();

create or replace function private.generate_sla_alerts() returns integer language plpgsql security definer set search_path='' as $$
declare t record;r uuid;l text;total integer:=0;
begin
 for t in select x.*,b.owner_id from public.tasks x join public.boards b on b.id=x.board_id where x.archived_at is null and x.status<>'done' and x.sla_due_at is not null and now()>=x.sla_due_at-interval '60 minutes' loop
  l:=case when now()>=t.sla_due_at+interval '60 minutes' then 'escalated' when now()>=t.sla_due_at then 'breached' else 'warning' end;r:=case when l='escalated' then coalesce(t.accountable_owner_id,t.owner_id) else coalesce(t.assigned_to,t.accountable_owner_id,t.owner_id) end;
  insert into public.notifications(recipient_id,user_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key) values(r,r,t.id,t.board_id,'sla_'||l,case l when 'warning' then 'SLA próximo do limite' when 'breached' then 'SLA violado' else 'SLA escalonado' end,t.title,'/Boards/Details/'||t.board_id,case when l='warning' then 'normal' else 'high' end,'sla:'||t.id||':'||l) on conflict(deduplication_key) where deduplication_key is not null do nothing;
  update public.tasks set sla_level=l where id=t.id and sla_level is distinct from l;total:=total+1;
 end loop;return total;
end $$;
revoke execute on function private.generate_sla_alerts() from public,anon,authenticated;
grant execute on function private.generate_sla_alerts() to service_role;
create extension if not exists pg_cron with schema pg_catalog;
do $$begin if exists(select 1 from pg_extension where extname='pg_cron') then perform cron.unschedule(jobid) from cron.job where jobname='pulseboard-sla-alerts';perform cron.schedule('pulseboard-sla-alerts','*/15 * * * *','select private.generate_sla_alerts()');end if;exception when others then raise notice 'Cron SLA: %',sqlerrm;end$$;

create or replace function public.execute_task_automations() returns trigger language plpgsql security definer set search_path='' as $$
declare r record;target uuid;days integer;
begin
 if pg_trigger_depth()>1 then return new;end if;
 for r in select * from public.automations a where a.is_active and (a.board_id is null or a.board_id=new.board_id) and ((a.trigger_type='status_change' and new.status is distinct from old.status and a.trigger_value=new.status) or (a.trigger_type='priority_change' and new.priority is distinct from old.priority and a.trigger_value=new.priority) or (a.trigger_type='assignment_change' and new.assigned_to is distinct from old.assigned_to)) loop
  if r.action_type='assign_user' then begin target:=r.action_payload::uuid;new.assigned_to=target;exception when invalid_text_representation then null;end;
  elsif r.action_type='move_status' then new.status=r.action_payload;
  elsif r.action_type='set_priority' and r.action_payload in('low','medium','high','critical') then new.priority=r.action_payload;
  elsif r.action_type='set_due_days' then begin days:=r.action_payload::integer;new.due_date=current_date+days;exception when invalid_text_representation then null;end;end if;
  if r.action_type='notify_manager' then
   select owner_id into target from public.boards where id=new.board_id;
   insert into public.notifications(recipient_id,user_id,actor_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
   values(target,target,(select auth.uid()),new.id,new.board_id,'automation','Automação: '||r.title,new.title,'/Boards/Details/'||new.board_id,'normal','automation:'||r.id||':'||new.id||':'||to_char(now(),'YYYYMMDDHH24'))
   on conflict(deduplication_key) where deduplication_key is not null do nothing;
  end if;
  if (select auth.uid()) is not null then insert into public.activity_log(task_id,board_id,user_id,action,details) values(new.id,new.board_id,(select auth.uid()),'automation_fired',jsonb_build_object('automation',r.title,'action',r.action_type));end if;
 end loop;return new;
end $$;
drop trigger if exists execute_task_automations on public.tasks;
create trigger execute_task_automations before update of status,priority,assigned_to on public.tasks for each row execute function public.execute_task_automations();
revoke execute on function public.execute_task_automations() from public,anon,authenticated;
