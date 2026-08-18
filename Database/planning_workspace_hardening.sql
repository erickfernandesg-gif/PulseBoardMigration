-- Endurecimento final do planejamento: privilegios minimos, politicas sem sobreposicao e indices de relacionamento.
-- Execute depois de planning_workspace_upgrade.sql.

begin;

-- A assinatura antiga nao considera o time do projeto e nao deve continuar executavel.
drop function if exists private.business_due(timestamptz, integer);

create index if not exists company_holidays_created_by_idx on public.company_holidays(created_by);
create index if not exists company_holidays_team_idx on public.company_holidays(team_id);
create index if not exists portfolio_dependencies_created_by_idx on public.portfolio_dependencies(created_by);
create index if not exists portfolio_dependencies_predecessor_task_idx
  on public.portfolio_dependencies(predecessor_task_id) where predecessor_task_id is not null;
create index if not exists portfolio_dependencies_successor_task_idx
  on public.portfolio_dependencies(successor_task_id) where successor_task_id is not null;
create index if not exists project_baselines_created_by_idx on public.project_baselines(created_by);
create index if not exists recurring_task_rules_assigned_to_idx on public.recurring_task_rules(assigned_to);
create index if not exists recurring_task_rules_created_by_idx on public.recurring_task_rules(created_by);
create index if not exists recurring_task_rules_template_idx on public.recurring_task_rules(template_id);
create index if not exists task_templates_created_by_idx on public.task_templates(created_by);

-- Politicas FOR ALL tambem participam do SELECT. A separacao abaixo preserva a leitura
-- e evita avaliacoes permissivas duplicadas, mantendo alteracoes exclusivas de gestores.
drop policy if exists holidays_manage on public.company_holidays;
create policy holidays_insert on public.company_holidays for insert to authenticated
  with check((select public.is_admin()) or ((select public.is_manager()) and team_id=(select public.current_team_id())));
create policy holidays_update on public.company_holidays for update to authenticated
  using((select public.is_admin()) or ((select public.is_manager()) and team_id=(select public.current_team_id())))
  with check((select public.is_admin()) or ((select public.is_manager()) and team_id=(select public.current_team_id())));
create policy holidays_delete on public.company_holidays for delete to authenticated
  using((select public.is_admin()) or ((select public.is_manager()) and team_id=(select public.current_team_id())));

drop policy if exists baselines_manage on public.project_baselines;
create policy baselines_insert on public.project_baselines for insert to authenticated with check((select public.is_manager()));
create policy baselines_update on public.project_baselines for update to authenticated
  using((select public.is_manager())) with check((select public.is_manager()));
create policy baselines_delete on public.project_baselines for delete to authenticated using((select public.is_manager()));

drop policy if exists portfolio_dependencies_manage on public.portfolio_dependencies;
create policy portfolio_dependencies_insert on public.portfolio_dependencies for insert to authenticated
  with check((select public.is_manager()));
create policy portfolio_dependencies_update on public.portfolio_dependencies for update to authenticated
  using((select public.is_manager())) with check((select public.is_manager()));
create policy portfolio_dependencies_delete on public.portfolio_dependencies for delete to authenticated
  using((select public.is_manager()));

drop policy if exists templates_manage on public.task_templates;
create policy templates_insert on public.task_templates for insert to authenticated with check((select public.is_manager()));
create policy templates_update on public.task_templates for update to authenticated
  using((select public.is_manager())) with check((select public.is_manager()));
create policy templates_delete on public.task_templates for delete to authenticated using((select public.is_manager()));

drop policy if exists recurring_manage on public.recurring_task_rules;
create policy recurring_insert on public.recurring_task_rules for insert to authenticated with check((select public.is_manager()));
create policy recurring_update on public.recurring_task_rules for update to authenticated
  using((select public.is_manager())) with check((select public.is_manager()));
create policy recurring_delete on public.recurring_task_rules for delete to authenticated using((select public.is_manager()));

commit;
