create or replace function private.prepare_task_assignment_change()
returns trigger language plpgsql set search_path='' as $$
begin
  if new.assigned_to is distinct from old.assigned_to then
    new.workflow_state:=case when new.assigned_to=(select auth.uid()) then 'in_progress' else 'inbox' end;
  end if;
  return new;
end $$;
drop trigger if exists prepare_task_assignment_change on public.tasks;
create trigger prepare_task_assignment_change before update of assigned_to on public.tasks
for each row execute function private.prepare_task_assignment_change();

create or replace function public.notify_task_assignment_change()
returns trigger language plpgsql security definer set search_path=public as $$
declare actor uuid := auth.uid();
begin
  if tg_op='INSERT' and new.assigned_to is not null and actor is not null then
    insert into public.task_assignments(task_id,to_user_id,assigned_by,stage,status,accepted_at,estimated_minutes,due_date)
    values(new.id,new.assigned_to,actor,new.status,
      case when actor=new.assigned_to then 'accepted' else 'pending' end,
      case when actor=new.assigned_to then now() else null end,new.estimated_minutes,new.due_date);
  end if;
  if tg_op='UPDATE' and new.assigned_to is not null and old.assigned_to is distinct from new.assigned_to and actor is not null
    and not exists(select 1 from public.task_assignments where task_id=new.id and to_user_id=new.assigned_to and status in ('pending','accepted')) then
    update public.task_assignments set status='completed',completed_at=now(),updated_at=now()
      where task_id=new.id and status in ('pending','accepted');
    insert into public.task_assignments(task_id,from_user_id,to_user_id,assigned_by,stage,status,accepted_at,estimated_minutes,due_date)
      values(new.id,old.assigned_to,new.assigned_to,actor,new.status,
        case when actor=new.assigned_to then 'accepted' else 'pending' end,
        case when actor=new.assigned_to then now() else null end,new.estimated_minutes,new.due_date);
  end if;
  if new.assigned_to is not null and (actor is null or actor<>new.assigned_to)
    and (tg_op='INSERT' or old.assigned_to is distinct from new.assigned_to) then
    if tg_op='UPDATE' and old.assigned_to is not null then
      insert into public.task_followers(task_id,user_id,reason)
      values(new.id,old.assigned_to,'previous_assignee') on conflict(task_id,user_id) do nothing;
    end if;
    insert into public.notifications(user_id,recipient_id,actor_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
    values(new.assigned_to,new.assigned_to,actor,new.id,new.board_id,'assignment_received','Nova tarefa atribuída',new.title,
      '/Boards/Details/'||new.board_id::text,'high','assignment:'||new.id::text||':'||new.assigned_to::text||':'||new.updated_at::text)
    on conflict(deduplication_key) do nothing;
  end if;
  return new;
end $$;
