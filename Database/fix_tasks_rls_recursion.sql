-- Corrige o ciclo tasks -> task_collaborators/task_followers -> tasks.
-- A função fica fora do schema exposto e valida explicitamente a identidade da sessão.
create schema if not exists private;
revoke all on schema private from public, anon;
grant usage on schema private to authenticated;

create or replace function private.task_is_participant(target_task_id uuid)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
  select (select auth.uid()) is not null
    and (
      exists (
        select 1
        from public.task_collaborators c
        where c.task_id = target_task_id
          and c.user_id = (select auth.uid())
      )
      or exists (
        select 1
        from public.task_followers f
        where f.task_id = target_task_id
          and f.user_id = (select auth.uid())
      )
    );
$$;

revoke execute on function private.task_is_participant(uuid) from public, anon, service_role;
grant execute on function private.task_is_participant(uuid) to authenticated;

-- Remove policies antigas, amplas e duplicadas que anulavam as regras granulares.
drop policy if exists "Gestores veem todas as tarefas" on public.tasks;
drop policy if exists "Tasks are viewable by authenticated users." on public.tasks;
drop policy if exists "Users can delete tasks." on public.tasks;
drop policy if exists "Users can insert tasks." on public.tasks;
drop policy if exists "Users can update tasks." on public.tasks;
drop policy if exists tasks_read on public.tasks;

create policy tasks_read
on public.tasks
for select
to authenticated
using (
  (select public.is_admin())
  or assigned_to = (select auth.uid())
  or accountable_owner_id = (select auth.uid())
  or created_by = (select auth.uid())
  or exists (
    select 1 from public.boards b
    where b.id = tasks.board_id
      and b.owner_id = (select auth.uid())
  )
  or (select private.task_is_participant(tasks.id))
  or (assigned_to is not null and (select public.can_manage_user(assigned_to)))
);

create index if not exists idx_task_collaborators_task_user
  on public.task_collaborators(task_id, user_id);
create index if not exists idx_task_followers_task_user
  on public.task_followers(task_id, user_id);
