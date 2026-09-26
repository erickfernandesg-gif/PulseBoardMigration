-- Reabre um apontamento aprovado ou rejeitado, mas ainda não faturado, para a fila de revisão.

create or replace function private.reopen_billing_time_log_impl(p_log_id uuid, p_requester_id uuid)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
  target public.time_logs%rowtype;
  task_board uuid;
  actor uuid := (select auth.uid());
begin
  if actor is null or actor <> p_requester_id then
    raise exception 'Solicitante inválido.' using errcode = '42501';
  end if;

  select l.* into target
  from public.time_logs l
  where l.id = p_log_id
  for update;

  select t.board_id into task_board
  from public.tasks t
  where t.id = target.task_id;

  if target.id is null or not private.can_edit_board(task_board) then
    raise exception 'Sem permissão para reabrir este apontamento.' using errcode = '42501';
  end if;

  if target.approval_status not in ('approved', 'rejected') or target.billing_status <> 'unbilled' then
    raise exception 'Somente apontamentos aprovados ou rejeitados, ainda não faturados, podem ser reabertos.' using errcode = '42501';
  end if;

  perform set_config('app.billing_operation', 'billing_review', true);
  update public.time_logs
  set approval_status = 'pending', approved_by = null, approved_at = null
  where id = target.id;

  insert into public.activity_log(task_id, board_id, user_id, action, details)
  values (target.task_id, task_board, actor, 'billing_time_log_reopened', '{}'::jsonb);
end;
$$;

create or replace function public.reopen_billing_time_log(p_log_id uuid, p_requester_id uuid)
returns void
language sql
security definer
set search_path = ''
as $$
  select private.reopen_billing_time_log_impl(p_log_id, p_requester_id);
$$;

revoke execute on function private.reopen_billing_time_log_impl(uuid, uuid), public.reopen_billing_time_log(uuid, uuid) from public, anon;
grant execute on function public.reopen_billing_time_log(uuid, uuid) to authenticated, service_role;
