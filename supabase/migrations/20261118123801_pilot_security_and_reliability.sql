-- Substitui políticas legadas sem remover registros de negócio.
do $$ declare rule record; begin
  for rule in select policyname from pg_policies where schemaname='public' and tablename='task_comments' loop
    execute format('drop policy %I on public.task_comments',rule.policyname);
  end loop;
end $$;
create policy comments_select on public.task_comments for select to authenticated
using(exists(select 1 from public.tasks t where t.id=task_id));
create policy comments_insert on public.task_comments for insert to authenticated
with check(user_id=(select auth.uid()) and exists(select 1 from public.tasks t where t.id=task_id));
create policy comments_update_own on public.task_comments for update to authenticated
using(user_id=(select auth.uid()) and deleted_at is null and exists(select 1 from public.tasks t where t.id=task_id))
with check(user_id=(select auth.uid()) and exists(select 1 from public.tasks t where t.id=task_id));
create policy comments_delete_own on public.task_comments for delete to authenticated
using(user_id=(select auth.uid()) and exists(select 1 from public.tasks t where t.id=task_id));

-- Seguir uma tarefa não pode ser usado para conceder acesso a si próprio.
drop policy if exists followers_insert on public.task_followers;
create policy followers_insert on public.task_followers for insert to authenticated
with check((select private.can_edit_task(task_id)) and exists(select 1 from public.profiles p where p.id=user_id and p.is_active));

create or replace function public.is_admin() returns boolean language sql stable security definer set search_path='' as $$
select exists(select 1 from public.profiles where id=(select auth.uid()) and role='admin' and is_active);
$$;
create or replace function public.is_manager() returns boolean language sql stable security definer set search_path='' as $$
select exists(select 1 from public.profiles where id=(select auth.uid()) and role in ('admin','manager') and is_active);
$$;
create or replace function public.current_team_id() returns uuid language sql stable security definer set search_path='' as $$
select team_id from public.profiles where id=(select auth.uid()) and is_active;
$$;
create or replace function public.can_manage_user(target_user uuid) returns boolean language sql stable security definer set search_path='' as $$
select public.is_admin() or exists(select 1 from public.profiles me join public.profiles target on target.id=target_user
where me.id=(select auth.uid()) and me.is_active and me.role='manager' and me.team_id is not null and me.team_id=target.team_id);
$$;

-- Decisões e configuração de aprovação só passam por rotinas autorizadas.
drop policy if exists approval_steps_insert on public.task_approval_steps;
drop policy if exists approval_steps_update on public.task_approval_steps;
drop policy if exists approval_steps_delete on public.task_approval_steps;
drop policy if exists approval_steps_manage on public.task_approval_steps;
revoke insert,update,delete on public.task_approval_steps from authenticated,anon;

create or replace function private.can_review_approval(target_step_id uuid)
returns boolean language sql stable security definer set search_path='' as $$
select exists(select 1 from public.task_approval_steps s join public.tasks t on t.id=s.task_id
join public.profiles me on me.id=(select auth.uid()) and me.is_active where s.id=target_step_id and
(private.can_edit_board(t.board_id) or s.approver_id=me.id or exists(select 1 from public.approval_delegations d
where d.delegator_id=s.approver_id and d.substitute_id=me.id and d.is_active and current_date between d.starts_on and d.ends_on)));
$$;

create or replace function private.add_task_approval_step_impl(p_task_id uuid,p_sequence integer,p_approver_id uuid)
returns uuid language plpgsql security definer set search_path='' as $$
declare task_row public.tasks; step_id uuid;
begin
 select * into task_row from public.tasks where id=p_task_id for update;
 if task_row.id is null or not private.can_edit_board(task_row.board_id) then
  raise exception 'Sem permissão para configurar aprovações.' using errcode='42501'; end if;
 if p_sequence is null or p_sequence<1 or not exists(select 1 from public.profiles where id=p_approver_id and is_active) then
  raise exception 'Sequência ou aprovador inválido.' using errcode='22023'; end if;
 if task_row.archived_at is not null or task_row.status='done' then raise exception 'A tarefa está encerrada.'; end if;
 insert into public.task_approval_steps(task_id,sequence,approver_id,status) values(p_task_id,p_sequence,p_approver_id,'waiting') returning id into step_id;
 perform private.activate_task_approval(p_task_id,false);
 return step_id;
end $$;
create or replace function public.add_task_approval_step(p_task_id uuid,p_sequence integer,p_approver_id uuid)
returns uuid language sql security invoker set search_path='' as $$ select private.add_task_approval_step_impl(p_task_id,p_sequence,p_approver_id); $$;

create or replace function private.delete_task_approval_step_impl(p_step_id uuid)
returns void language plpgsql security definer set search_path='' as $$
declare step_row public.task_approval_steps; task_row public.tasks;
begin
 select * into step_row from public.task_approval_steps where id=p_step_id;
 select * into task_row from public.tasks where id=step_row.task_id for update;
 if task_row.id is null or not private.can_edit_board(task_row.board_id) then
  raise exception 'Sem permissão para configurar aprovações.' using errcode='42501'; end if;
 if step_row.status in ('approved','rejected') then raise exception 'Preserve o histórico das decisões já registradas.'; end if;
 delete from public.task_approval_steps where id=p_step_id;
 if exists(select 1 from public.task_approval_steps where task_id=task_row.id and status in ('waiting','pending')) then
  perform private.activate_task_approval(task_row.id,false);
 elsif task_row.workflow_state='waiting_review' and task_row.acceptance_by is null then
  update public.tasks set workflow_state='inbox' where id=task_row.id;
 end if;
end $$;
create or replace function public.delete_task_approval_step(p_step_id uuid)
returns void language sql security invoker set search_path='' as $$ select private.delete_task_approval_step_impl(p_step_id); $$;
revoke all on function public.add_task_approval_step(uuid,integer,uuid),public.delete_task_approval_step(uuid),
private.add_task_approval_step_impl(uuid,integer,uuid),private.delete_task_approval_step_impl(uuid) from public,anon;
grant execute on function public.add_task_approval_step(uuid,integer,uuid),public.delete_task_approval_step(uuid),
private.add_task_approval_step_impl(uuid,integer,uuid),private.delete_task_approval_step_impl(uuid) to authenticated;

-- Um gestor só pode delegar aprovações dentro do seu escopo de pessoas.
drop policy if exists delegations_insert on public.approval_delegations;
drop policy if exists delegations_update on public.approval_delegations;
drop policy if exists delegations_delete on public.approval_delegations;
create policy delegations_insert on public.approval_delegations for insert to authenticated
with check((delegator_id=(select auth.uid()) or public.can_manage_user(delegator_id)) and created_by=(select auth.uid())
and delegator_id<>substitute_id and ends_on>=starts_on and exists(select 1 from public.profiles where id=substitute_id and is_active));
create policy delegations_update on public.approval_delegations for update to authenticated
using(delegator_id=(select auth.uid()) or public.can_manage_user(delegator_id))
with check((delegator_id=(select auth.uid()) or public.can_manage_user(delegator_id)) and delegator_id<>substitute_id and ends_on>=starts_on);
create policy delegations_delete on public.approval_delegations for delete to authenticated
using(delegator_id=(select auth.uid()) or public.can_manage_user(delegator_id));

-- Arquivos gerais ficam separados das imagens de conversa (que continuam limitadas a 8 MB).
insert into storage.buckets(id,name,public,file_size_limit,allowed_mime_types)
values('task-files','task-files',false,26214400,null)
on conflict(id) do update set public=false,file_size_limit=excluded.file_size_limit,allowed_mime_types=null;

-- Geração única de prazos, usada pelo job e pela consulta do próprio usuário.
create or replace function private.generate_due_notifications(p_recipient uuid default null)
returns integer language plpgsql security definer set search_path='' as $$
declare inserted_count integer;
begin
 insert into public.notifications(recipient_id,user_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
 select recipients.id,recipients.id,t.id,t.board_id,
 case when t.due_date<now() then 'task_overdue' else 'task_due_soon' end,
 case when t.due_date<now() then 'Tarefa atrasada' else 'Prazo próximo' end,t.title,'/Boards/Details/'||t.board_id,
 case when t.due_date<now() then 'critical' else 'high' end,
 (case when t.due_date<now() then 'overdue:' else 'due:' end)||t.id||':'||recipients.id||':'||(now() at time zone 'America/Sao_Paulo')::date
 from public.tasks t join public.boards b on b.id=t.board_id
 cross join lateral(select distinct v.id from (values(t.assigned_to),(t.accountable_owner_id)) v(id) where v.id is not null) recipients
 join public.profiles person on person.id=recipients.id and person.is_active
 left join public.notification_preferences preference on preference.user_id=recipients.id
 where t.archived_at is null and b.status<>'archived' and t.status<>'done' and t.due_date<now()+interval '24 hours'
 and (p_recipient is null or recipients.id=p_recipient) and coalesce(preference.in_app,true) and coalesce(preference.due_reminders,true)
 on conflict do nothing;
 get diagnostics inserted_count=row_count;
 return inserted_count;
end $$;
create or replace function public.ensure_due_notifications()
returns integer language plpgsql security definer set search_path='' as $$
begin
 if not exists(select 1 from public.profiles where id=(select auth.uid()) and is_active) then
  raise exception 'Autenticação necessária.' using errcode='42501'; end if;
 return private.generate_due_notifications((select auth.uid()));
end $$;
create or replace function private.generate_operational_notifications()
returns void language plpgsql security definer set search_path='' as $$
begin
 perform private.generate_due_notifications();
 insert into public.notifications(recipient_id,user_id,board_id,type,title,message,action_url,priority,deduplication_key)
 select b.owner_id,b.owner_id,b.id,'budget_threshold','Alerta de orçamento',b.name||' atingiu '||round(x.spent/nullif(b.budget_amount,0)*100)||'% do orçamento',
 '/Executive','critical','budget:'||b.id||':'||floor(x.spent/nullif(b.budget_amount,0)*10)
 from public.boards b join public.profiles owner on owner.id=b.owner_id and owner.is_active
 cross join lateral(select coalesce(sum(l.cost_rate_snapshot*l.minutes/60.0),0) spent
 from public.tasks t join public.time_logs l on l.task_id=t.id where t.board_id=b.id)x
 left join public.notification_preferences p on p.user_id=b.owner_id
 where b.status<>'archived' and b.budget_amount>0 and x.spent>=b.budget_amount*b.budget_warning_percent/100.0
 and coalesce(p.in_app,true) and coalesce(p.budget_alerts,true) on conflict do nothing;
end $$;
revoke all on function private.generate_due_notifications(uuid),private.generate_operational_notifications() from public,anon,authenticated,service_role;
revoke all on function public.ensure_due_notifications() from public,anon;
grant execute on function public.ensure_due_notifications() to authenticated,service_role;
select cron.schedule('pulseboard-operational-alerts','*/10 * * * *','select private.generate_operational_notifications()');
select cron.schedule('pulseboard-recurring-tasks','*/15 * * * *','select private.generate_recurring_tasks()');
