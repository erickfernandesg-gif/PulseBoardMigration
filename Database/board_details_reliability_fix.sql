-- Correções de confiabilidade para o detalhe do Board: concorrência do Gantt
-- e exclusão definitiva, explícita e restrita, de tarefas já arquivadas.

create or replace function public.update_task_schedule_atomic(
  p_task_id uuid,
  p_expected_version bigint,
  p_start_date timestamptz,
  p_due_date timestamptz)
returns boolean
language plpgsql
security invoker
set search_path = public
as $$
declare
  target public.tasks%rowtype;
begin
  if p_task_id is null or p_expected_version is null or p_expected_version <= 0 then
    raise exception 'Versão da tarefa inválida. Recarregue a página.' using errcode = '22023';
  end if;
  if p_start_date is null or p_due_date is null or p_due_date::date < p_start_date::date then
    raise exception 'O prazo não pode ser anterior à data de início.' using errcode = '22023';
  end if;

  select * into target
  from public.tasks
  where id = p_task_id and archived_at is null
  for update;

  if target.id is null or not (select private.can_edit_task(p_task_id)) then
    raise exception 'Tarefa não encontrada ou sem permissão.' using errcode = '42501';
  end if;
  if target.row_version <> p_expected_version then
    raise exception 'Esta tarefa foi alterada por outra pessoa. Recarregue a página antes de reagendar.' using errcode = '40001';
  end if;

  update public.tasks
  set start_date = p_start_date::date,
      due_date = p_due_date::date,
      updated_at = now()
  where id = p_task_id;
  return true;
end;
$$;

create or replace function public.permanently_delete_archived_task(p_task_id uuid)
returns boolean
language plpgsql
security invoker
set search_path = public
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
  if exists (select 1 from public.tasks where parent_task_id = p_task_id) then
    raise exception 'Exclua ou restaure as subtarefas antes de excluir esta tarefa definitivamente.' using errcode = '23503';
  end if;
  if exists (
    select 1
    from public.task_dependencies dependency
    join public.tasks successor on successor.id = dependency.task_id
    where dependency.depends_on_task_id = p_task_id and successor.archived_at is null
  ) then
    raise exception 'A tarefa ainda é pré-requisito de uma atividade ativa.' using errcode = '23503';
  end if;

  -- O histórico desta tarefa não deve sobreviver sem a referência da tarefa.
  delete from public.activity_log where task_id = p_task_id;
  delete from public.tasks where id = p_task_id;
  return true;
end;
$$;

revoke execute on function public.update_task_schedule_atomic(uuid,bigint,timestamptz,timestamptz),
  public.permanently_delete_archived_task(uuid) from public, anon;
grant execute on function public.update_task_schedule_atomic(uuid,bigint,timestamptz,timestamptz),
  public.permanently_delete_archived_task(uuid) to authenticated, service_role;

notify pgrst, 'reload schema';
