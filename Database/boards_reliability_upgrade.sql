-- Confiabilidade dos Boards: autorização consolidada, integridade e operações atômicas.
create schema if not exists private;
revoke all on schema private from public, anon;
grant usage on schema private to authenticated;
alter table public.tasks add column if not exists row_version bigint not null default 1;
create or replace function private.increment_task_version()
returns trigger language plpgsql set search_path='' as $$ begin new.row_version:=old.row_version+1; return new; end $$;
drop trigger if exists increment_task_version on public.tasks;
create trigger increment_task_version before update on public.tasks for each row execute function private.increment_task_version();

create or replace function private.can_read_task(target_task_id uuid)
returns boolean language sql stable security definer set search_path=''
as $$
  select (select auth.uid()) is not null and exists(
    select 1 from public.tasks t
    join public.boards b on b.id=t.board_id
    join public.profiles me on me.id=(select auth.uid()) and me.is_active
    left join public.profiles assignee on assignee.id=t.assigned_to
    left join public.profiles owner on owner.id=b.owner_id
    where t.id=target_task_id and (
      me.role='admin' or b.owner_id=me.id or t.assigned_to=me.id or t.accountable_owner_id=me.id or t.created_by=me.id
      or exists(select 1 from public.task_collaborators c where c.task_id=t.id and c.user_id=me.id)
      or exists(select 1 from public.task_followers f where f.task_id=t.id and f.user_id=me.id)
      or (me.role='manager' and me.team_id is not null and me.team_id in (assignee.team_id,owner.team_id))
    )
  );
$$;

create or replace function private.can_edit_task(target_task_id uuid)
returns boolean language sql stable security definer set search_path=''
as $$
  select (select auth.uid()) is not null and exists(
    select 1 from public.tasks t
    join public.boards b on b.id=t.board_id
    join public.profiles me on me.id=(select auth.uid()) and me.is_active
    left join public.profiles assignee on assignee.id=t.assigned_to
    left join public.profiles owner on owner.id=b.owner_id
    where t.id=target_task_id and (
      me.role='admin' or b.owner_id=me.id or t.assigned_to=me.id or t.accountable_owner_id=me.id or t.created_by=me.id
      or exists(select 1 from public.task_collaborators c where c.task_id=t.id and c.user_id=me.id)
      or (me.role='manager' and me.team_id is not null and me.team_id in (assignee.team_id,owner.team_id))
    )
  );
$$;

create or replace function private.can_edit_board(target_board_id uuid)
returns boolean language sql stable security definer set search_path=''
as $$
  select (select auth.uid()) is not null and exists(
    select 1 from public.boards b
    join public.profiles me on me.id=(select auth.uid()) and me.is_active
    join public.profiles owner on owner.id=b.owner_id
    where b.id=target_board_id and (
      me.role='admin' or b.owner_id=me.id
      or (me.role='manager' and me.team_id is not null and me.team_id=owner.team_id)
    )
  );
$$;

create or replace function private.can_read_board(target_board_id uuid)
returns boolean language sql stable security definer set search_path=''
as $$
  select private.can_edit_board(target_board_id) or exists(
    select 1 from public.tasks t where t.board_id=target_board_id and private.can_read_task(t.id)
  );
$$;

revoke execute on function private.can_read_task(uuid), private.can_edit_task(uuid),
  private.can_read_board(uuid), private.can_edit_board(uuid) from public,anon,service_role;
grant execute on function private.can_read_task(uuid), private.can_edit_task(uuid),
  private.can_read_board(uuid), private.can_edit_board(uuid) to authenticated;

create or replace function private.validate_board_integrity()
returns trigger language plpgsql security definer set search_path='' as $$
begin
  new.name:=trim(new.name);
  if new.name='' or length(new.name)>120 then raise exception 'O nome do quadro deve ter entre 1 e 120 caracteres'; end if;
  if new.planned_start is not null and new.planned_end is not null and new.planned_end<new.planned_start then
    raise exception 'O fim planejado não pode ser anterior ao início';
  end if;
  if new.budget_amount is not null and new.budget_amount<0 then raise exception 'Orçamento inválido'; end if;
  if not exists(select 1 from public.profiles p where p.id=new.owner_id and p.is_active) then
    raise exception 'O responsável pelo quadro está inativo ou não existe';
  end if;
  if jsonb_typeof(coalesce(new.settings,'[]'::jsonb))<>'array' or jsonb_array_length(coalesce(new.settings,'[]'::jsonb))=0 then
    raise exception 'O quadro precisa possuir ao menos uma etapa';
  end if;
  if exists(select 1 from jsonb_array_elements(new.settings) s where nullif(trim(s->>'id'),'') is null or nullif(trim(s->>'title'),'') is null)
    or (select count(*) from jsonb_array_elements(new.settings))<>(select count(distinct s->>'id') from jsonb_array_elements(new.settings) s) then
    raise exception 'As etapas do quadro devem ter identificadores e nomes únicos';
  end if;
  return new;
end $$;
drop trigger if exists validate_board_integrity on public.boards;
create trigger validate_board_integrity before insert or update on public.boards
for each row execute function private.validate_board_integrity();

create or replace function private.validate_task_integrity()
returns trigger language plpgsql security definer set search_path='' as $$
declare previous_status text;
begin
  new.title:=trim(new.title);
  if new.title='' or length(new.title)>200 then raise exception 'O título deve ter entre 1 e 200 caracteres'; end if;
  if new.start_date is not null and new.due_date is not null and new.due_date<new.start_date then
    raise exception 'O prazo não pode ser anterior à data de início';
  end if;
  if new.estimated_minutes<0 or new.total_minutes_spent<0 or coalesce(new.sla_minutes,0)<0 then raise exception 'Tempos inválidos'; end if;
  if new.custom_fields is null or jsonb_typeof(new.custom_fields)<>'object' then raise exception 'Campos personalizados inválidos'; end if;
  if not exists(select 1 from public.boards b, jsonb_array_elements(b.settings) s where b.id=new.board_id and s->>'id'=new.status) then
    raise exception 'A etapa selecionada não existe neste quadro';
  end if;
  if new.assigned_to is not null and not exists(select 1 from public.profiles p where p.id=new.assigned_to and p.is_active) then
    raise exception 'O responsável selecionado está inativo ou não existe';
  end if;
  if new.acceptance_by is not null and not exists(select 1 from public.profiles p where p.id=new.acceptance_by and p.is_active) then
    raise exception 'O aprovador selecionado está inativo ou não existe';
  end if;
  if new.parent_task_id is not null and not exists(select 1 from public.tasks p where p.id=new.parent_task_id and p.board_id=new.board_id and p.id<>new.id) then
    raise exception 'A tarefa principal deve pertencer ao mesmo quadro';
  end if;
  if tg_op='UPDATE' then
    if new.board_id<>old.board_id or new.created_by is distinct from old.created_by or new.parent_task_id is distinct from old.parent_task_id then
      raise exception 'Não é permitido alterar quadro, criador ou vínculo da subtarefa';
    end if;
    previous_status:=old.status;
  end if;
  if new.status='done' and (tg_op='INSERT' or previous_status is distinct from 'done') then
    if exists(select 1 from public.task_dependencies d join public.tasks p on p.id=d.depends_on_task_id
      where d.task_id=new.id and p.status<>'done' and p.archived_at is null) then
      raise exception 'Existem dependências ainda não concluídas';
    end if;
    if exists(select 1 from public.task_checklists c where c.task_id=new.id and not c.is_completed) then
      raise exception 'Conclua todos os itens do checklist antes de finalizar';
    end if;
    if new.acceptance_by is not null and new.accepted_at is null then
      raise exception 'Esta tarefa precisa passar pela aprovação configurada';
    end if;
    new.completed_at:=coalesce(new.completed_at,now()); new.workflow_state:='done';
  elsif tg_op='UPDATE' and old.status='done' and new.status<>'done' then
    new.completed_at:=null; new.accepted_at:=null;
    if new.workflow_state='done' then new.workflow_state:=case when new.assigned_to is null then 'waiting_external' else 'in_progress' end; end if;
  end if;
  if tg_op='INSERT' then
    perform pg_advisory_xact_lock(hashtextextended(new.board_id::text||':'||new.status,0));
    select coalesce(max(t.position_index),-1)+1 into new.position_index from public.tasks t
      where t.board_id=new.board_id and t.status=new.status and t.archived_at is null;
  end if;
  new.updated_at:=now();
  return new;
end $$;
drop trigger if exists validate_task_integrity on public.tasks;
create trigger validate_task_integrity before insert or update on public.tasks
for each row execute function private.validate_task_integrity();

create or replace function private.protect_assignment_identity()
returns trigger language plpgsql set search_path='' as $$
begin
  if new.task_id<>old.task_id or new.from_user_id is distinct from old.from_user_id or new.to_user_id<>old.to_user_id
    or new.assigned_by<>old.assigned_by or new.created_at<>old.created_at then
    raise exception 'Os participantes da atribuição não podem ser alterados';
  end if;
  return new;
end $$;
drop trigger if exists protect_assignment_identity on public.task_assignments;
create trigger protect_assignment_identity before update on public.task_assignments
for each row execute function private.protect_assignment_identity();

-- Consolida policies e remove permissões legadas que anulavam a autorização granular.
do $$ declare item record; begin
  for item in select schemaname,tablename,policyname from pg_policies
    where schemaname='public' and tablename in ('boards','tasks','task_collaborators','task_followers','task_dependencies','task_assignments')
  loop execute format('drop policy %I on %I.%I',item.policyname,item.schemaname,item.tablename); end loop;
end $$;

create policy boards_read on public.boards for select to authenticated using((select private.can_read_board(id)));
create policy boards_insert on public.boards for insert to authenticated
  with check(owner_id=(select auth.uid()) and exists(select 1 from public.profiles p where p.id=(select auth.uid()) and p.is_active));
create policy boards_update on public.boards for update to authenticated
  using((select private.can_edit_board(id))) with check((select private.can_edit_board(id)));
create policy boards_delete on public.boards for delete to authenticated using((select private.can_edit_board(id)));

create policy tasks_read on public.tasks for select to authenticated using((select private.can_read_task(id)));
create policy tasks_insert on public.tasks for insert to authenticated with check(
  created_by=(select auth.uid()) and archived_at is null and (select private.can_read_board(board_id)));
create policy tasks_update on public.tasks for update to authenticated
  using((select private.can_edit_task(id))) with check((select private.can_edit_task(id)));
create policy tasks_delete on public.tasks for delete to authenticated using((select private.can_edit_board(board_id)));

create policy collaborators_read on public.task_collaborators for select to authenticated using((select private.can_read_task(task_id)));
create policy collaborators_insert on public.task_collaborators for insert to authenticated with check(
  (select private.can_edit_task(task_id)) and exists(select 1 from public.profiles p where p.id=user_id and p.is_active));
create policy collaborators_delete on public.task_collaborators for delete to authenticated using((select private.can_edit_task(task_id)));

create policy followers_read on public.task_followers for select to authenticated using(user_id=(select auth.uid()) or (select private.can_read_task(task_id)));
create policy followers_insert on public.task_followers for insert to authenticated with check(
  (user_id=(select auth.uid()) or (select private.can_edit_task(task_id))) and exists(select 1 from public.profiles p where p.id=user_id and p.is_active));
create policy followers_delete on public.task_followers for delete to authenticated using(user_id=(select auth.uid()) or (select private.can_edit_task(task_id)));

create policy dependencies_read on public.task_dependencies for select to authenticated using((select private.can_read_task(task_id)));
create policy dependencies_insert on public.task_dependencies for insert to authenticated with check(
  (select private.can_edit_task(task_id)) and (select private.can_read_task(depends_on_task_id)));
create policy dependencies_delete on public.task_dependencies for delete to authenticated using((select private.can_edit_task(task_id)));

create policy assignments_read on public.task_assignments for select to authenticated using(
  to_user_id=(select auth.uid()) or from_user_id=(select auth.uid()) or assigned_by=(select auth.uid()) or (select private.can_read_task(task_id)));
create policy assignments_insert on public.task_assignments for insert to authenticated with check(
  assigned_by=(select auth.uid()) and (select private.can_edit_task(task_id))
  and exists(select 1 from public.profiles p where p.id=to_user_id and p.is_active));
create policy assignments_update on public.task_assignments for update to authenticated
  using(to_user_id=(select auth.uid()) or (select private.can_edit_task(task_id)))
  with check(to_user_id=(select auth.uid()) or (select private.can_edit_task(task_id)));

create or replace function public.create_task_atomic(
  p_task_id uuid,p_board_id uuid,p_title text,p_description text,p_status text,p_priority text,
  p_start_date timestamptz,p_due_date timestamptz,p_assigned_to uuid,p_accountable_owner_id uuid,
  p_created_by uuid,p_workflow_state text,p_client_id uuid,p_target_month text,p_estimated_minutes integer,
  p_parent_task_id uuid,p_collaborator_ids uuid[])
returns uuid language plpgsql security invoker set search_path=public as $$
declare collaborator uuid;
begin
  if p_created_by is distinct from (select auth.uid()) then raise exception 'Criador inválido'; end if;
  insert into public.tasks(id,board_id,title,description,status,priority,start_date,due_date,assigned_to,
    accountable_owner_id,created_by,workflow_state,client_id,target_month,estimated_minutes,parent_task_id,created_at,updated_at)
  values(p_task_id,p_board_id,p_title,p_description,p_status,p_priority,p_start_date,p_due_date,p_assigned_to,
    p_accountable_owner_id,p_created_by,p_workflow_state,p_client_id,p_target_month,greatest(0,p_estimated_minutes),p_parent_task_id,now(),now());
  foreach collaborator in array coalesce(p_collaborator_ids,'{}'::uuid[]) loop
    if collaborator is not null and collaborator<>p_assigned_to then
      insert into public.task_collaborators(task_id,user_id,role,created_at) values(p_task_id,collaborator,'collaborator',now())
      on conflict(task_id,user_id) do nothing;
    end if;
  end loop;
  return p_task_id;
end $$;

drop function if exists public.update_task_atomic(uuid,text,text,text,text,timestamptz,timestamptz,uuid,uuid,text,integer,integer,numeric,jsonb,boolean,text,uuid[]);
drop function if exists public.update_task_atomic(uuid,timestamptz,text,text,text,text,timestamptz,timestamptz,uuid,uuid,text,integer,integer,numeric,jsonb,boolean,text,uuid[]);
create or replace function public.update_task_atomic(
  p_task_id uuid,p_expected_version bigint,p_title text,p_description text,p_status text,p_priority text,p_start_date timestamptz,p_due_date timestamptz,
  p_assigned_to uuid,p_client_id uuid,p_target_month text,p_estimated_minutes integer,p_sla_minutes integer,
  p_planned_value numeric,p_custom_fields jsonb,p_is_blocked boolean,p_blocker_reason text,p_collaborator_ids uuid[])
returns uuid language plpgsql security invoker set search_path=public as $$
declare collaborator uuid; current_version bigint;
begin
  select row_version into current_version from public.tasks where id=p_task_id and archived_at is null for update;
  if not found then raise exception 'Tarefa não encontrada ou sem permissão'; end if;
  if p_expected_version<=0 or current_version<>p_expected_version then
    raise exception 'Esta tarefa foi alterada por outra pessoa. Recarregue a página antes de salvar.' using errcode='40001';
  end if;
  update public.tasks set title=p_title,description=p_description,status=p_status,priority=p_priority,start_date=p_start_date,
    due_date=p_due_date,assigned_to=p_assigned_to,client_id=p_client_id,target_month=p_target_month,
    estimated_minutes=greatest(0,p_estimated_minutes),sla_minutes=p_sla_minutes,planned_value=p_planned_value,
    custom_fields=coalesce(p_custom_fields,'{}'::jsonb),is_blocked=p_is_blocked,
    blocker_reason=case when p_is_blocked then nullif(trim(p_blocker_reason),'') else null end,updated_at=now()
  where id=p_task_id;
  delete from public.task_collaborators where task_id=p_task_id;
  foreach collaborator in array coalesce(p_collaborator_ids,'{}'::uuid[]) loop
    if collaborator is not null and collaborator<>p_assigned_to then
      insert into public.task_collaborators(task_id,user_id,role,created_at) values(p_task_id,collaborator,'collaborator',now())
      on conflict(task_id,user_id) do nothing;
    end if;
  end loop;
  return p_task_id;
end $$;

create or replace function private.move_task_atomic(p_task_id uuid,p_new_status text,p_position integer)
returns boolean language plpgsql security definer set search_path='' as $$
declare current_task public.tasks%rowtype; target_position integer;
begin
  if (select auth.uid()) is null or not private.can_edit_task(p_task_id) then raise exception 'Tarefa não encontrada ou sem permissão'; end if;
  select * into current_task from public.tasks where id=p_task_id and archived_at is null for update;
  if current_task.id is null then raise exception 'Tarefa não encontrada ou sem permissão'; end if;
  perform 1 from public.tasks where board_id=current_task.board_id and archived_at is null for update;
  target_position:=greatest(0,least(p_position,(select count(*)::integer from public.tasks where board_id=current_task.board_id and status=p_new_status and archived_at is null)));
  if current_task.status=p_new_status then
    if target_position>current_task.position_index then
      update public.tasks set position_index=position_index-1 where board_id=current_task.board_id and status=p_new_status and archived_at is null and position_index>current_task.position_index and position_index<=target_position;
    elsif target_position<current_task.position_index then
      update public.tasks set position_index=position_index+1 where board_id=current_task.board_id and status=p_new_status and archived_at is null and position_index>=target_position and position_index<current_task.position_index;
    end if;
  else
    update public.tasks set position_index=position_index-1 where board_id=current_task.board_id and status=current_task.status and archived_at is null and position_index>current_task.position_index;
    update public.tasks set position_index=position_index+1 where board_id=current_task.board_id and status=p_new_status and archived_at is null and position_index>=target_position;
  end if;
  update public.tasks set status=p_new_status,position_index=target_position,updated_at=now() where id=p_task_id;
  return true;
end $$;

revoke execute on function private.move_task_atomic(uuid,text,integer) from public,anon,service_role;
grant execute on function private.move_task_atomic(uuid,text,integer) to authenticated;
create or replace function public.move_task_atomic(p_task_id uuid,p_new_status text,p_position integer)
returns boolean language sql security invoker set search_path=''
as $$ select private.move_task_atomic(p_task_id,p_new_status,p_position) $$;

create or replace function public.archive_task(p_task_id uuid)
returns boolean language plpgsql security invoker set search_path=public as $$
declare target public.tasks%rowtype;
begin
  select * into target from public.tasks where id=p_task_id for update;
  if target.id is null or not (select private.can_edit_board(target.board_id)) then raise exception 'Sem permissão para arquivar esta tarefa'; end if;
  if exists(with recursive subtree as (select id from public.tasks where id=p_task_id union all select t.id from public.tasks t join subtree s on t.parent_task_id=s.id)
    select 1 from public.task_dependencies d join subtree s on s.id=d.depends_on_task_id join public.tasks successor on successor.id=d.task_id
    where successor.archived_at is null and not exists(select 1 from subtree own where own.id=successor.id)) then
    raise exception 'A tarefa é pré-requisito de outra atividade ativa';
  end if;
  with recursive subtree as (select id from public.tasks where id=p_task_id union all select t.id from public.tasks t join subtree s on t.parent_task_id=s.id)
  update public.tasks set archived_at=now(),workflow_state='cancelled',updated_at=now() where id in(select id from subtree);
  update public.task_assignments set status='cancelled',updated_at=now() where task_id=p_task_id and status in('pending','accepted');
  insert into public.activity_log(task_id,board_id,user_id,action,details) values(p_task_id,target.board_id,(select auth.uid()),'task_archived','{}');
  return true;
end $$;

create or replace function public.restore_task(p_task_id uuid)
returns boolean language plpgsql security invoker set search_path=public as $$
declare target public.tasks%rowtype;
begin
  select * into target from public.tasks where id=p_task_id for update;
  if target.id is null or not (select private.can_edit_board(target.board_id)) then raise exception 'Sem permissão para restaurar esta tarefa'; end if;
  update public.tasks set archived_at=null,workflow_state=case when assigned_to is null then 'waiting_external' else 'inbox' end,
    position_index=(select coalesce(max(t.position_index),-1)+1 from public.tasks t where t.board_id=target.board_id and t.status=target.status and t.archived_at is null),updated_at=now()
  where id=p_task_id;
  return true;
end $$;

revoke execute on function public.create_task_atomic(uuid,uuid,text,text,text,text,timestamptz,timestamptz,uuid,uuid,uuid,text,uuid,text,integer,uuid,uuid[]),
 public.update_task_atomic(uuid,bigint,text,text,text,text,timestamptz,timestamptz,uuid,uuid,text,integer,integer,numeric,jsonb,boolean,text,uuid[]),
 public.move_task_atomic(uuid,text,integer),public.archive_task(uuid),public.restore_task(uuid) from public,anon;
grant execute on function public.create_task_atomic(uuid,uuid,text,text,text,text,timestamptz,timestamptz,uuid,uuid,uuid,text,uuid,text,integer,uuid,uuid[]),
 public.update_task_atomic(uuid,bigint,text,text,text,text,timestamptz,timestamptz,uuid,uuid,text,integer,integer,numeric,jsonb,boolean,text,uuid[]),
 public.move_task_atomic(uuid,text,integer),public.archive_task(uuid),public.restore_task(uuid) to authenticated;

-- Corrige dados legados seguros e normaliza posições.
update public.tasks set completed_at=coalesce(updated_at,created_at,now()),workflow_state='done' where status='done' and completed_at is null;
with ranked as (select id,row_number() over(partition by board_id,status order by position_index,created_at,id)-1 new_position from public.tasks where archived_at is null)
update public.tasks t set position_index=r.new_position from ranked r where r.id=t.id and t.position_index<>r.new_position;
create index if not exists idx_tasks_board_status_position_active on public.tasks(board_id,status,position_index) where archived_at is null;
create index if not exists idx_boards_owner on public.boards(owner_id);
create index if not exists idx_notifications_actor on public.notifications(actor_id) where actor_id is not null;
create index if not exists idx_notifications_board on public.notifications(board_id) where board_id is not null;
create index if not exists idx_notifications_task on public.notifications(task_id) where task_id is not null;
create index if not exists idx_notifications_legacy_user on public.notifications(user_id);
create index if not exists idx_assignments_acceptance_by on public.task_assignments(acceptance_by) where acceptance_by is not null;
create index if not exists idx_assignments_assigned_by on public.task_assignments(assigned_by);
create index if not exists idx_assignments_from_user on public.task_assignments(from_user_id) where from_user_id is not null;
create index if not exists idx_collaborators_user on public.task_collaborators(user_id);
create index if not exists idx_dependencies_predecessor on public.task_dependencies(depends_on_task_id);
create index if not exists idx_followers_user on public.task_followers(user_id);
create index if not exists idx_tasks_acceptance_by on public.tasks(acceptance_by) where acceptance_by is not null;
create index if not exists idx_tasks_accountable_owner on public.tasks(accountable_owner_id) where accountable_owner_id is not null;
create index if not exists idx_tasks_client on public.tasks(client_id) where client_id is not null;
create index if not exists idx_tasks_created_by on public.tasks(created_by) where created_by is not null;

-- O schema legado criava o mesmo trigger de automação com dois nomes.
drop trigger if exists trg_execute_automations on public.tasks;

create or replace function private.audit_task_mutation()
returns trigger language plpgsql security definer set search_path='' as $$
declare changed text[]:='{}';
begin
  if (select auth.uid()) is null then return new; end if;
  if tg_op='INSERT' then
    insert into public.activity_log(task_id,board_id,user_id,action,details)
    values(new.id,new.board_id,(select auth.uid()),'task_created',jsonb_build_object('title',new.title,'assigned_to',new.assigned_to));
    return new;
  end if;
  if new.title is distinct from old.title then changed:=array_append(changed,'title'); end if;
  if new.description is distinct from old.description then changed:=array_append(changed,'description'); end if;
  if new.assigned_to is distinct from old.assigned_to then changed:=array_append(changed,'assigned_to'); end if;
  if new.due_date is distinct from old.due_date or new.start_date is distinct from old.start_date then changed:=array_append(changed,'schedule'); end if;
  if new.estimated_minutes is distinct from old.estimated_minutes then changed:=array_append(changed,'estimate'); end if;
  if new.archived_at is distinct from old.archived_at then changed:=array_append(changed,case when new.archived_at is null then 'restored' else 'archived' end); end if;
  if cardinality(changed)>0 then
    insert into public.activity_log(task_id,board_id,user_id,action,details)
    values(new.id,new.board_id,(select auth.uid()),'task_updated',jsonb_build_object('fields',changed));
  end if;
  return new;
end $$;
drop trigger if exists audit_task_mutation on public.tasks;
create trigger audit_task_mutation after insert or update on public.tasks
for each row execute function private.audit_task_mutation();
