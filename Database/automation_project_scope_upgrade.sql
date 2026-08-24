-- Restringe automações ao projeto onde foram configuradas.
-- Regras globais legadas são pausadas para evitar efeitos inesperados.
update public.automations set is_active=false where board_id is null and is_active=true;
update public.automations set trigger_value='any' where trigger_type='assignment_change';

create or replace function public.execute_task_automations() returns trigger language plpgsql security definer set search_path='' as $$
declare r record;target uuid;days integer;
begin
 if pg_trigger_depth()>1 then return new;end if;
 for r in select * from public.automations a where a.is_active and a.board_id=new.board_id and ((a.trigger_type='status_change' and new.status is distinct from old.status and a.trigger_value=new.status) or (a.trigger_type='priority_change' and new.priority is distinct from old.priority and a.trigger_value=new.priority) or (a.trigger_type='assignment_change' and new.assigned_to is distinct from old.assigned_to and a.trigger_value='any')) loop
  if r.action_type='assign_user' then begin target:=r.action_payload::uuid;exception when invalid_text_representation then null;end;
  elsif r.action_type='move_status' then new.status=r.action_payload;
  elsif r.action_type='set_priority' and r.action_payload in('low','medium','high','critical') then new.priority=r.action_payload;
  elsif r.action_type='set_due_days' then begin days:=r.action_payload::integer;new.due_date=current_date+days;exception when invalid_text_representation then null;end;end if;
  if r.action_type='notify_manager' then
   select owner_id into target from public.boards where id=new.board_id;
   insert into public.notifications(recipient_id,user_id,actor_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
   values(target,target,(select auth.uid()),new.id,new.board_id,'automation','Automação: '||r.title,new.title,'/Boards/Details/'||new.board_id,'normal','automation:'||r.id||':'||new.id||':'||to_char(now(),'YYYYMMDDHH24'))
   on conflict(deduplication_key) where deduplication_key is not null do nothing;
  end if;
  if (select auth.uid()) is not null then insert into public.activity_log(task_id,board_id,user_id,action,details) values(new.id,new.board_id,(select auth.uid()),'automation_fired',jsonb_build_object('automation',r.title,'action',r.action_type));end if;
 end loop;return new;
end $$;
drop trigger if exists execute_task_automations on public.tasks;
create trigger execute_task_automations before update of status,priority,assigned_to on public.tasks for each row execute function public.execute_task_automations();
revoke execute on function public.execute_task_automations() from public,anon,authenticated;
