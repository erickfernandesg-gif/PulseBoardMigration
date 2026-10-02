-- Gestão operacional: schedules só podem ser lidos/alterados pela própria pessoa,
-- pelo gestor da equipe correspondente ou por administrador.
-- Execute depois de planning_audit_corrections.sql, em uma transação.

begin;

drop policy if exists schedules_read on public.work_schedules;
drop policy if exists schedules_manage on public.work_schedules;

create policy schedules_read on public.work_schedules for select to authenticated using(
  user_id=(select auth.uid())
  or (select public.is_admin())
  or (user_id is not null and (select public.can_manage_user(user_id)))
  or (team_id is not null and (select public.is_manager()) and team_id=(select public.current_team_id()))
);

create policy schedules_manage on public.work_schedules for all to authenticated
  using(
    (select public.is_admin())
    or (user_id is not null and (select public.can_manage_user(user_id)))
    or (team_id is not null and (select public.is_manager()) and team_id=(select public.current_team_id()))
  )
  with check(
    (select public.is_admin())
    or (user_id is not null and (select public.can_manage_user(user_id)))
    or (team_id is not null and (select public.is_manager()) and team_id=(select public.current_team_id()))
  );

commit;
