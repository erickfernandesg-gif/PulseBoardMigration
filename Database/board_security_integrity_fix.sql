-- Correções da auditoria de Boards: escopo de auditoria, aprovações e exclusão definitiva.
-- Aplicar no Supabase após deploy do código desta mesma alteração.

begin;

-- 1. O feed de atividade deve obedecer à mesma visibilidade da tarefa/Board.
drop policy if exists "Activity log viewable by authenticated users." on public.activity_log;
drop policy if exists "Users can insert activity log." on public.activity_log;
drop policy if exists activity_read on public.activity_log;
drop policy if exists activity_insert on public.activity_log;

create policy activity_log_read_scoped on public.activity_log
for select to authenticated
using (
  (
    task_id is not null and exists (
      select 1
      from public.tasks task
      where task.id = activity_log.task_id
        and task.board_id = activity_log.board_id
        and private.can_read_task(task.id)
    )
  )
  or
  (
    task_id is null
    and board_id is not null
    and private.can_read_board(board_id)
  )
);

-- Escritas de rotinas existentes continuam possíveis somente para atividades
-- relacionadas a uma tarefa que o ator pode consultar. Funções SECURITY DEFINER
-- continuam registrando auditoria de operações internas.
create policy activity_log_insert_scoped on public.activity_log
for insert to authenticated
with check (
  user_id = (select auth.uid())
  and (
    (
      task_id is not null and exists (
        select 1
        from public.tasks task
        where task.id = activity_log.task_id
          and task.board_id = activity_log.board_id
          and private.can_read_task(task.id)
      )
    )
    or
    (
      task_id is null
      and board_id is not null
      and private.can_edit_board(board_id)
    )
  )
);

-- 2. Não permitir que exclusões físicas removam apontamentos ou seu vínculo
-- financeiro por cascata. A regra também protege exclusões de Board.
create or replace function private.prevent_task_delete_with_time_logs()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  if exists (select 1 from public.time_logs log where log.task_id = old.id) then
    raise exception 'Não é possível excluir definitivamente uma tarefa que possui apontamentos de horas. Mantenha-a arquivada para preservar o histórico financeiro.'
      using errcode = '23503';
  end if;
  return old;
end;
$$;

drop trigger if exists prevent_task_delete_with_time_logs on public.tasks;
create trigger prevent_task_delete_with_time_logs
before delete on public.tasks
for each row execute function private.prevent_task_delete_with_time_logs();

revoke all on function private.prevent_task_delete_with_time_logs() from public, anon, authenticated;

create or replace function public.permanently_delete_archived_task(p_task_id uuid)
returns boolean
language plpgsql
set search_path = 'public'
as $$
declare
  target public.tasks%rowtype;
begin
  select * into target from public.tasks where id = p_task_id for update;
  if target.id is null or not (select private.can_edit_board(target.board_id)) then
    raise exception 'Tarefa não encontrada ou sem permissão.' using errcode = '42501';
  end if;
  if target.archived_at is null then
    raise exception 'Arquive a tarefa antes de excluí-la definitivamente.' using errcode = '22023';
  end if;
  if exists (select 1 from public.time_logs where task_id = p_task_id) then
    raise exception 'Não é possível excluir uma tarefa com apontamentos de horas. Mantenha-a arquivada para preservar o histórico financeiro.' using errcode = '23503';
  end if;
  if exists (select 1 from public.tasks where parent_task_id = p_task_id) then
    raise exception 'Exclua ou restaure as subtarefas antes de excluir esta tarefa definitivamente.' using errcode = '23503';
  end if;
  if exists (
    select 1
    from public.task_dependencies dependency
    join public.tasks successor on successor.id = dependency.task_id
    where dependency.depends_on_task_id = p_task_id
      and successor.archived_at is null
  ) then
    raise exception 'A tarefa ainda é pré-requisito de uma atividade ativa.' using errcode = '23503';
  end if;

  delete from public.activity_log where task_id = p_task_id;
  delete from public.tasks where id = p_task_id;
  return true;
end;
$$;

-- 3. Um gerente pode decidir aprovações apenas nos Boards que efetivamente administra.
create or replace function private.decide_task_approval_impl(
  p_step_id uuid,
  p_decision text,
  p_note text default null
)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
  step_row public.task_approval_steps;
  actor uuid := (select auth.uid());
  effective_approver uuid;
  next_step record;
  task_board_id uuid;
  task_title text;
begin
  if actor is null then
    raise exception 'Autenticação necessária.' using errcode = '42501';
  end if;

  select * into step_row
  from public.task_approval_steps
  where id = p_step_id
  for update;
  if step_row.id is null or step_row.status <> 'pending' or p_decision not in ('approve', 'reject') then
    raise exception 'Etapa inválida.';
  end if;

  select board_id, title into task_board_id, task_title
  from public.tasks
  where id = step_row.task_id;
  if task_board_id is null then
    raise exception 'Tarefa da aprovação não encontrada.' using errcode = '23503';
  end if;

  select coalesce(
    (
      select delegation.substitute_id
      from public.approval_delegations delegation
      where delegation.delegator_id = step_row.approver_id
        and delegation.is_active
        and current_date between delegation.starts_on and delegation.ends_on
      order by delegation.created_at desc
      limit 1
    ),
    step_row.approver_id
  ) into effective_approver;

  if actor <> effective_approver and not private.can_edit_board(task_board_id) then
    raise exception 'Aprovação pertence a outro usuário ou a um projeto fora da sua gestão.' using errcode = '42501';
  end if;

  update public.task_approval_steps
  set status = case when p_decision = 'approve' then 'approved' else 'rejected' end,
      decision_by = actor,
      decision_note = nullif(trim(p_note), ''),
      decided_at = now()
  where id = step_row.id;

  if p_decision = 'reject' then
    update public.tasks
    set workflow_state = 'changes_requested',
        is_blocked = true,
        blocker_reason = coalesce(nullif(trim(p_note), ''), 'Aprovação rejeitada')
    where id = step_row.task_id;
  else
    select id, approver_id, sequence into next_step
    from public.task_approval_steps
    where task_id = step_row.task_id
      and sequence > step_row.sequence
      and status = 'waiting'
    order by sequence
    limit 1
    for update;

    if next_step.id is not null then
      update public.task_approval_steps set status = 'pending' where id = next_step.id;
      select coalesce(
        (
          select delegation.substitute_id
          from public.approval_delegations delegation
          where delegation.delegator_id = next_step.approver_id
            and delegation.is_active
            and current_date between delegation.starts_on and delegation.ends_on
          order by delegation.created_at desc
          limit 1
        ),
        next_step.approver_id
      ) into effective_approver;
      insert into public.notifications(
        recipient_id, user_id, actor_id, task_id, board_id, type, title, message,
        action_url, priority, deduplication_key
      )
      values(
        effective_approver, effective_approver, actor, step_row.task_id, task_board_id,
        'approval_required', 'Aprovação pendente', task_title,
        '/Boards/Details/' || task_board_id, 'high', 'approval:' || next_step.id
      )
      on conflict(deduplication_key) where deduplication_key is not null do update
      set recipient_id = excluded.recipient_id,
          user_id = excluded.user_id,
          actor_id = excluded.actor_id,
          read_at = null,
          archived_at = null,
          created_at = now();
    else
      update public.tasks
      set workflow_state = case when status = 'done' then 'done' else 'accepted' end,
          is_blocked = false,
          blocker_reason = null
      where id = step_row.task_id;
    end if;
  end if;

  insert into public.activity_log(task_id, board_id, user_id, action, details)
  values(
    step_row.task_id, task_board_id, actor, 'approval_decided',
    jsonb_build_object('sequence', step_row.sequence, 'decision', p_decision, 'note', p_note)
  );
end;
$$;

revoke execute on function public.permanently_delete_archived_task(uuid) from public, anon;
grant execute on function public.permanently_delete_archived_task(uuid) to authenticated, service_role;
notify pgrst, 'reload schema';

commit;
