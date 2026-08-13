create or replace function public.respond_task_assignment(p_assignment_id uuid,p_action text,p_note text default null)
returns boolean language plpgsql security invoker set search_path=public as $$
declare assignment public.task_assignments%rowtype; current_task public.tasks%rowtype;
begin
  select * into assignment from public.task_assignments where id=p_assignment_id for update;
  if assignment.id is null then raise exception 'Atribuição não encontrada'; end if;
  if assignment.to_user_id<>auth.uid() and not public.can_manage_user(assignment.to_user_id) then raise exception 'Sem permissão'; end if;
  if assignment.status not in ('pending','accepted') then raise exception 'Esta atribuição já foi encerrada'; end if;
  select * into current_task from public.tasks where id=assignment.task_id for update;

  if p_action='accept' then
    if assignment.status<>'pending' then raise exception 'Somente atribuições pendentes podem ser aceitas'; end if;
    if exists(select 1 from public.task_dependencies d join public.tasks prerequisite on prerequisite.id=d.depends_on_task_id
      where d.task_id=assignment.task_id and prerequisite.status<>'done' and prerequisite.archived_at is null) then
      raise exception 'Existem dependências ainda não concluídas';
    end if;
    update public.task_assignments set status='accepted',accepted_at=now(),response_note=nullif(trim(p_note),''),updated_at=now() where id=p_assignment_id;
    update public.tasks set workflow_state='in_progress' where id=assignment.task_id;
  elsif p_action='reject' then
    if nullif(trim(p_note),'') is null then raise exception 'Informe o motivo da recusa'; end if;
    update public.task_assignments set status='rejected',response_note=trim(p_note),updated_at=now() where id=p_assignment_id;
    insert into public.task_comments(task_id,user_id,content,message_type,created_at)
    values(assignment.task_id,(select auth.uid()),trim(p_note),'question',now());
    if assignment.from_user_id is not null then
      insert into public.task_assignments(task_id,from_user_id,to_user_id,assigned_by,stage,status,notes,due_date,estimated_minutes)
      values(assignment.task_id,assignment.to_user_id,assignment.from_user_id,(select auth.uid()),current_task.status,'pending',
        trim(p_note),current_task.due_date,current_task.estimated_minutes);
    end if;
    update public.tasks set assigned_to=assignment.from_user_id,workflow_state='changes_requested' where id=assignment.task_id;
  elsif p_action='complete' then
    if assignment.status<>'accepted' then raise exception 'Aceite a atribuição antes de concluí-la'; end if;
    update public.task_assignments set status='completed',completed_at=now(),response_note=nullif(trim(p_note),''),updated_at=now() where id=p_assignment_id;
    if assignment.requires_acceptance and assignment.acceptance_by is not null then
      update public.tasks set assigned_to=assignment.acceptance_by,workflow_state='waiting_review' where id=assignment.task_id;
    else
      update public.tasks set workflow_state='done',status='done',completed_at=now() where id=assignment.task_id;
    end if;
  else raise exception 'Ação inválida'; end if;
  insert into public.activity_log(task_id,board_id,user_id,action,details)
  values(assignment.task_id,current_task.board_id,(select auth.uid()),'assignment_'||p_action,
    jsonb_build_object('assignment_id',assignment.id,'note',p_note));
  perform private.emit_assignment_response(assignment.id,p_action);
  return true;
end $$;

revoke execute on function public.respond_task_assignment(uuid,text,text) from public,anon;
grant execute on function public.respond_task_assignment(uuid,text,text) to authenticated;
