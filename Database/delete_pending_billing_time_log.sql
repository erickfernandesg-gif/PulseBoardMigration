-- Exclusão protegida de apontamentos ainda em revisão.
-- A função privada mantém a validação de sessão, gestão do board e estado do registro.

create or replace function private.delete_pending_billing_time_log_impl(p_log_id uuid, p_requester_id uuid)
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
    raise exception 'Sem permissão para excluir este apontamento.' using errcode = '42501';
  end if;

  if target.approval_status <> 'pending' or target.billing_status <> 'unbilled' then
    raise exception 'Somente apontamentos pendentes e não faturados podem ser excluídos.' using errcode = '42501';
  end if;

  insert into public.activity_log(task_id, board_id, user_id, action, details)
  values (
    target.task_id,
    task_board,
    actor,
    'billing_time_log_deleted',
    jsonb_build_object('minutes', target.minutes, 'is_billable', target.is_billable)
  );

  delete from public.time_logs where id = target.id;
end;
$$;

create or replace function public.delete_pending_billing_time_log(p_log_id uuid, p_requester_id uuid)
returns void
language sql
security definer
set search_path = ''
as $$
  select private.delete_pending_billing_time_log_impl(p_log_id, p_requester_id);
$$;

revoke execute on function private.delete_pending_billing_time_log_impl(uuid, uuid), public.delete_pending_billing_time_log(uuid, uuid) from public, anon;
grant execute on function public.delete_pending_billing_time_log(uuid, uuid) to authenticated, service_role;

-- Gestores usam a RPC acima, que valida o board e o estado antes de excluir.
-- A exclusão direta fica limitada ao próprio apontamento ainda pendente.
drop policy if exists time_logs_delete on public.time_logs;
create policy time_logs_delete on public.time_logs
for delete to authenticated
using (
  user_id = (select auth.uid())
  and approval_status = 'pending'
  and billing_status = 'unbilled'
);
