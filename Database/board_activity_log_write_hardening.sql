-- Impede a criação direta de eventos de auditoria pelo Data API.
-- As rotinas de domínio abaixo validam o ator e passam a registrar atividade
-- como SECURITY DEFINER, com caminho de pesquisa imutável.

begin;

drop policy if exists activity_log_insert_scoped on public.activity_log;
create policy activity_log_insert_no_direct_access on public.activity_log
for insert to authenticated
with check (false);

create or replace function public.archive_task(p_task_id uuid)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare target public.tasks%rowtype;
begin
  select * into target from public.tasks where id=p_task_id for update;
  if target.id is null or not private.can_edit_board(target.board_id) then
    raise exception 'Sem permissão para arquivar esta tarefa';
  end if;
  if exists(with recursive subtree as (select id from public.tasks where id=p_task_id union all select task.id from public.tasks task join subtree parent on task.parent_task_id=parent.id)
    select 1 from public.task_dependencies dependency join subtree source on source.id=dependency.depends_on_task_id join public.tasks successor on successor.id=dependency.task_id
    where successor.archived_at is null and not exists(select 1 from subtree own where own.id=successor.id)) then
    raise exception 'A tarefa é pré-requisito de outra atividade ativa';
  end if;
  with recursive subtree as (select id from public.tasks where id=p_task_id union all select task.id from public.tasks task join subtree parent on task.parent_task_id=parent.id)
  update public.tasks set archived_at=now(),workflow_state='cancelled',updated_at=now() where id in(select id from subtree);
  update public.task_assignments set status='cancelled',updated_at=now() where task_id=p_task_id and status in('pending','accepted');
  insert into public.activity_log(task_id,board_id,user_id,action,details) values(p_task_id,target.board_id,(select auth.uid()),'task_archived','{}');
  return true;
end $$;

create or replace function public.handoff_task(
  p_task_id uuid, p_to_user_id uuid, p_stage text, p_due_date timestamptz default null,
  p_estimated_minutes integer default 0, p_notes text default null,
  p_acceptance_criteria text default null, p_requires_acceptance boolean default false,
  p_acceptance_by uuid default null
)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare current_task public.tasks%rowtype; assignment_id uuid;
begin
  select * into current_task from public.tasks where id=p_task_id for update;
  if current_task.id is null then raise exception 'Tarefa não encontrada'; end if;
  if not private.can_edit_task(p_task_id) then raise exception 'Sem permissão'; end if;
  if p_to_user_id is null then raise exception 'Novo executor obrigatório'; end if;

  update public.task_assignments set status='completed',completed_at=now(),updated_at=now()
  where task_id=p_task_id and status in ('pending','accepted');
  insert into public.task_assignments(task_id,from_user_id,to_user_id,assigned_by,stage,notes,acceptance_criteria,due_date,
    estimated_minutes,requires_acceptance,acceptance_by)
  values(p_task_id,current_task.assigned_to,p_to_user_id,(select auth.uid()),trim(p_stage),nullif(trim(p_notes),''),
    nullif(trim(p_acceptance_criteria),''),p_due_date,greatest(0,p_estimated_minutes),p_requires_acceptance,
    case when p_requires_acceptance then coalesce(p_acceptance_by,current_task.accountable_owner_id) else null end)
  returning id into assignment_id;

  if current_task.assigned_to is not null then
    insert into public.task_followers(task_id,user_id,reason) values(p_task_id,current_task.assigned_to,'handoff')
    on conflict(task_id,user_id) do nothing;
  end if;
  if current_task.accountable_owner_id is not null then
    insert into public.task_followers(task_id,user_id,reason) values(p_task_id,current_task.accountable_owner_id,'accountable')
    on conflict(task_id,user_id) do nothing;
  end if;

  update public.tasks set assigned_to=p_to_user_id,status=trim(p_stage),workflow_state='inbox',due_date=coalesce(p_due_date,due_date),
    estimated_minutes=case when p_estimated_minutes>0 then p_estimated_minutes else estimated_minutes end,
    acceptance_by=case when p_requires_acceptance then coalesce(p_acceptance_by,current_task.accountable_owner_id) else null end
  where id=p_task_id;
  insert into public.activity_log(task_id,board_id,user_id,action,details)
  values(p_task_id,current_task.board_id,(select auth.uid()),'task_handed_off',jsonb_build_object('from',current_task.assigned_to,'to',p_to_user_id,'stage',p_stage));
  return assignment_id;
end $$;

create or replace function public.log_task_comment_change()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare target_board uuid;
begin
  select board_id into target_board from public.tasks where id=new.task_id;
  insert into public.activity_log(task_id,board_id,user_id,action,details)
  values(new.task_id,target_board,(select auth.uid()),
    case when new.deleted_at is distinct from old.deleted_at then 'comment_deleted' else 'comment_edited' end,
    jsonb_build_object('comment_id',new.id));
  return new;
end $$;

create or replace function public.respond_task_assignment(p_assignment_id uuid,p_action text,p_note text default null)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare assignment public.task_assignments%rowtype; current_task public.tasks%rowtype;
begin
  select * into assignment from public.task_assignments where id=p_assignment_id for update;
  if assignment.id is null then raise exception 'Atribuição não encontrada'; end if;
  if assignment.to_user_id<>(select auth.uid()) and not public.can_manage_user(assignment.to_user_id) then raise exception 'Sem permissão'; end if;
  if assignment.status not in ('pending','accepted') then raise exception 'Esta atribuição já foi encerrada'; end if;
  select * into current_task from public.tasks where id=assignment.task_id for update;

  if p_action='accept' then
    if assignment.status<>'pending' then raise exception 'Somente atribuições pendentes podem ser aceitas'; end if;
    if exists(select 1 from public.task_dependencies dependency join public.tasks prerequisite on prerequisite.id=dependency.depends_on_task_id
      where dependency.task_id=assignment.task_id and prerequisite.status<>'done' and prerequisite.archived_at is null) then
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

create or replace function public.review_task(p_task_id uuid,p_action text,p_note text default null)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare current_task public.tasks%rowtype; previous_executor uuid;
begin
  select * into current_task from public.tasks where id=p_task_id for update;
  if current_task.id is null or current_task.workflow_state<>'waiting_review' then raise exception 'Tarefa não aguarda revisão'; end if;
  if current_task.acceptance_by<>(select auth.uid()) and not public.can_manage_user(current_task.acceptance_by) then raise exception 'Sem permissão'; end if;
  if p_action='approve' then
    update public.tasks set workflow_state='done',status='done',completed_at=now(),accepted_at=now() where id=p_task_id;
  elsif p_action='changes' then
    select to_user_id into previous_executor from public.task_assignments where task_id=p_task_id and status='completed' order by completed_at desc limit 1;
    if previous_executor is null then raise exception 'Executor anterior não encontrado'; end if;
    insert into public.task_assignments(task_id,from_user_id,to_user_id,assigned_by,stage,status,notes,due_date,estimated_minutes)
    values(p_task_id,(select auth.uid()),previous_executor,(select auth.uid()),current_task.status,'pending',nullif(trim(p_note),''),
      current_task.due_date,current_task.estimated_minutes);
    update public.tasks set assigned_to=previous_executor,workflow_state='changes_requested',completed_at=null where id=p_task_id;
  else raise exception 'Ação inválida'; end if;
  insert into public.activity_log(task_id,board_id,user_id,action,details)
  values(p_task_id,current_task.board_id,(select auth.uid()),'task_reviewed',jsonb_build_object('result',p_action,'note',p_note));
  return true;
end $$;

revoke execute on function public.log_task_comment_change() from public, anon, authenticated;
notify pgrst, 'reload schema';
commit;
