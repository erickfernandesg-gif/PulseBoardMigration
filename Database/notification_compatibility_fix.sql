-- Mantém compatibilidade entre a coluna legada user_id e o modelo atual recipient_id.
update public.notifications set recipient_id=user_id where recipient_id is null;

create or replace function private.sync_notification_recipient()
returns trigger language plpgsql set search_path='' as $$
begin
  new.recipient_id:=coalesce(new.recipient_id,new.user_id);
  new.user_id:=coalesce(new.user_id,new.recipient_id);
  if new.recipient_id is null then raise exception 'Destinatário da notificação obrigatório'; end if;
  if new.read_at is not null then new.read:=true;
  elsif coalesce(new.read,false) then
    if tg_op='UPDATE' then new.read_at:=coalesce(old.read_at,now()); else new.read_at:=now(); end if;
  end if;
  return new;
end $$;

drop trigger if exists sync_notification_recipient on public.notifications;
create trigger sync_notification_recipient before insert or update on public.notifications
for each row execute function private.sync_notification_recipient();

alter table public.notifications alter column recipient_id set not null;
create index if not exists idx_notifications_recipient_created
  on public.notifications(recipient_id,created_at desc) where archived_at is null;

drop policy if exists "Usuários marcam como lidas" on public.notifications;
drop policy if exists "Usuários veem as próprias notificações" on public.notifications;
drop policy if exists notifications_own on public.notifications;
drop policy if exists notifications_update_own on public.notifications;
drop policy if exists notifications_assignment_response on public.notifications;
create or replace function private.can_send_assignment_response(target_task_id uuid,target_recipient uuid)
returns boolean language sql stable security definer set search_path='' as $$
  select (select auth.uid()) is not null and exists(
    select 1 from public.task_assignments a where a.task_id=target_task_id
      and a.to_user_id=(select auth.uid()) and a.assigned_by=target_recipient
  );
$$;
revoke execute on function private.can_send_assignment_response(uuid,uuid) from public,anon,service_role;
grant execute on function private.can_send_assignment_response(uuid,uuid) to authenticated;
create or replace function private.emit_assignment_response(target_assignment_id uuid,response_action text)
returns void language plpgsql security definer set search_path='' as $$
declare target public.task_assignments%rowtype; target_task public.tasks%rowtype;
begin
  select * into target from public.task_assignments where id=target_assignment_id;
  select * into target_task from public.tasks where id=target.task_id;
  if target.id is null or (select auth.uid()) is null
    or (target.to_user_id<>(select auth.uid()) and not public.can_manage_user(target.to_user_id)) then
    raise exception 'Sem permissão para notificar esta atribuição';
  end if;
  if response_action not in ('accept','reject','complete') then raise exception 'Resposta inválida'; end if;
  if target.assigned_by<>(select auth.uid()) then
    insert into public.notifications(user_id,recipient_id,actor_id,task_id,board_id,type,title,message,action_url,deduplication_key)
    values(target.assigned_by,target.assigned_by,(select auth.uid()),target.task_id,target_task.board_id,'assignment_response',
      case response_action when 'accept' then 'Atribuição aceita' when 'reject' then 'Atribuição recusada' else 'Etapa concluída' end,
      target_task.title,'/Boards/Details/'||target_task.board_id::text,
      'assignment-response:'||target.id::text||':'||response_action)
    on conflict(deduplication_key) do nothing;
  end if;
end $$;
revoke execute on function private.emit_assignment_response(uuid,text) from public,anon,service_role;
grant execute on function private.emit_assignment_response(uuid,text) to authenticated;
create policy notifications_own on public.notifications for select to authenticated
  using(recipient_id=(select auth.uid()));
create policy notifications_update_own on public.notifications for update to authenticated
  using(recipient_id=(select auth.uid())) with check(recipient_id=(select auth.uid()));
create policy notifications_assignment_response on public.notifications for insert to authenticated with check(
  type='assignment_response' and actor_id=(select auth.uid())
  and (select private.can_send_assignment_response(task_id,recipient_id)));

revoke update on public.notifications from authenticated;
grant update(read_at,archived_at,read) on public.notifications to authenticated;
