-- Mantém estados explícitos das operações transacionais de handoff/revisão.
create or replace function private.prepare_task_assignment_change()
returns trigger language plpgsql set search_path='' as $$
begin
 if new.assigned_to is distinct from old.assigned_to
 and (new.workflow_state is not distinct from old.workflow_state or new.workflow_state in ('inbox','in_progress','waiting_external')) then
  new.workflow_state:=case when new.assigned_to is null then 'waiting_external'
    when new.assigned_to=(select auth.uid()) then 'in_progress' else 'inbox' end;
 end if;
 return new;
end $$;
-- O cron não possui JWT; a autoria da recorrência é a origem da atribuição.
do $$ declare definition text; begin
 definition:=pg_get_functiondef('public.notify_task_assignment_change()'::regprocedure);
 if position('actor uuid := auth.uid()' in definition)=0 then raise exception 'Versão inesperada do gatilho de atribuições.'; end if;
 execute replace(definition,'actor uuid := auth.uid()','actor uuid := coalesce(auth.uid(),new.created_by)');
end $$;
