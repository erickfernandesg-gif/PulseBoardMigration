-- Governança do planejamento: cada gestor altera somente itens do seu projeto ou equipe.
-- Execute depois de planning_workspace_hardening.sql, em uma transação, no Supabase.

begin;

drop policy if exists baselines_insert on public.project_baselines;
drop policy if exists baselines_update on public.project_baselines;
drop policy if exists baselines_delete on public.project_baselines;
create policy baselines_insert on public.project_baselines for insert to authenticated
  with check((select private.can_edit_board(board_id)));
create policy baselines_update on public.project_baselines for update to authenticated
  using((select private.can_edit_board(board_id)))
  with check((select private.can_edit_board(board_id)));
create policy baselines_delete on public.project_baselines for delete to authenticated
  using((select private.can_edit_board(board_id)));

drop policy if exists portfolio_dependencies_insert on public.portfolio_dependencies;
drop policy if exists portfolio_dependencies_update on public.portfolio_dependencies;
drop policy if exists portfolio_dependencies_delete on public.portfolio_dependencies;
create policy portfolio_dependencies_insert on public.portfolio_dependencies for insert to authenticated
  with check((select private.can_edit_board(predecessor_board_id)) and (select private.can_edit_board(successor_board_id)));
create policy portfolio_dependencies_update on public.portfolio_dependencies for update to authenticated
  using((select private.can_edit_board(predecessor_board_id)) and (select private.can_edit_board(successor_board_id)))
  with check((select private.can_edit_board(predecessor_board_id)) and (select private.can_edit_board(successor_board_id)));
create policy portfolio_dependencies_delete on public.portfolio_dependencies for delete to authenticated
  using((select private.can_edit_board(predecessor_board_id)) and (select private.can_edit_board(successor_board_id)));

drop policy if exists templates_read on public.task_templates;
create policy templates_read on public.task_templates for select to authenticated using(
  created_by=(select auth.uid()) or (
    is_active and (
      (board_id is null and team_id is null)
      or team_id=(select public.current_team_id())
      or (board_id is not null and (select private.can_read_board(board_id)))
      or (select public.is_admin())
    )
  )
);
drop policy if exists templates_insert on public.task_templates;
drop policy if exists templates_update on public.task_templates;
drop policy if exists templates_delete on public.task_templates;
create policy templates_insert on public.task_templates for insert to authenticated
  with check(
    (board_id is not null and (select private.can_edit_board(board_id)))
    or (board_id is null and ((select public.is_admin()) or team_id=(select public.current_team_id())) )
  );
create policy templates_update on public.task_templates for update to authenticated
  using(
    (board_id is not null and (select private.can_edit_board(board_id)))
    or (board_id is null and ((select public.is_admin()) or team_id=(select public.current_team_id())) )
  ) with check(
    (board_id is not null and (select private.can_edit_board(board_id)))
    or (board_id is null and ((select public.is_admin()) or team_id=(select public.current_team_id())) )
  );
create policy templates_delete on public.task_templates for delete to authenticated
  using(
    (board_id is not null and (select private.can_edit_board(board_id)))
    or (board_id is null and ((select public.is_admin()) or team_id=(select public.current_team_id())) )
  );

drop policy if exists recurring_insert on public.recurring_task_rules;
drop policy if exists recurring_update on public.recurring_task_rules;
drop policy if exists recurring_delete on public.recurring_task_rules;
create policy recurring_insert on public.recurring_task_rules for insert to authenticated
  with check((select private.can_edit_board(board_id)));
create policy recurring_update on public.recurring_task_rules for update to authenticated
  using((select private.can_edit_board(board_id)))
  with check((select private.can_edit_board(board_id)));
create policy recurring_delete on public.recurring_task_rules for delete to authenticated
  using((select private.can_edit_board(board_id)));

commit;
