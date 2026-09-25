-- Corrige a atribuição da linha bloqueada a um %rowtype.
-- Sem o "l.*", o PostgreSQL converte a linha inteira em texto e tenta usá-la
-- como UUID ao acessar target.task_id.

create or replace function private.review_billing_time_log_impl(p_log_id uuid, p_approve boolean, p_reviewer_id uuid)
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
  if actor is null or actor <> p_reviewer_id then
    raise exception 'Revisor inválido.' using errcode = '42501';
  end if;

  select l.* into target
  from public.time_logs l
  where l.id = p_log_id
  for update;

  select t.board_id into task_board
  from public.tasks t
  where t.id = target.task_id;

  if target.id is null or not private.can_edit_board(task_board) then
    raise exception 'Sem permissão para aprovar este apontamento.' using errcode = '42501';
  end if;

  if target.approval_status <> 'pending' or target.billing_status <> 'unbilled' then
    raise exception 'Somente apontamentos pendentes e não faturados podem ser revisados.';
  end if;

  perform set_config('app.billing_operation', 'billing_review', true);
  update public.time_logs
  set approval_status = case when p_approve then 'approved' else 'rejected' end,
      approved_by = actor,
      approved_at = now()
  where id = target.id;

  insert into public.activity_log(task_id, board_id, user_id, action, details)
  values (
    target.task_id,
    task_board,
    actor,
    'billing_time_review',
    jsonb_build_object('decision', case when p_approve then 'approved' else 'rejected' end)
  );
end;
$$;
