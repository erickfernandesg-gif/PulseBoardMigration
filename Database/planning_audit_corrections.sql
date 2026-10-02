-- Correções da auditoria da Central de gestão.
-- Execute depois de planning_governance_scope_fix.sql, em uma transação.
-- A alteração afeta apenas novas baselines; snapshots existentes preservam o registro histórico.

begin;

create or replace function public.capture_project_baseline(p_board_id uuid,p_name text default null)
returns uuid language plpgsql security invoker set search_path='' as $$
declare target public.boards%rowtype; baseline_id uuid; next_version integer; task_snapshot jsonb;
begin
  if (select auth.uid()) is null or not public.is_manager() then raise exception 'Somente gestores podem capturar baselines'; end if;
  perform pg_advisory_xact_lock(hashtextextended('baseline:'||p_board_id::text,0));
  select * into target from public.boards where id=p_board_id and status<>'archived' for update;
  if target.id is null then raise exception 'Projeto não encontrado ou arquivado'; end if;
  select coalesce(max(version),0)+1 into next_version from public.project_baselines where board_id=p_board_id;
  select coalesce(jsonb_agg(jsonb_build_object(
    'id',t.id,'title',t.title,'status',t.status,'priority',t.priority,'startDate',t.start_date,
    'dueDate',t.due_date,'assignedTo',t.assigned_to,'estimatedMinutes',t.estimated_minutes,
    'plannedValue',t.planned_value,'isBlocked',t.is_blocked) order by t.position_index,t.created_at),'[]'::jsonb)
    into task_snapshot from public.tasks t where t.board_id=p_board_id and t.archived_at is null;
  update public.project_baselines set is_active=false where board_id=p_board_id and is_active;
  insert into public.project_baselines(board_id,version,name,planned_start,planned_end,budget_amount,snapshot,is_active,created_by,created_at)
  values(p_board_id,next_version,coalesce(nullif(trim(p_name),''),'Baseline '||next_version),target.planned_start,target.planned_end,
    target.budget_amount,jsonb_build_object('tasks',task_snapshot,'taskCount',jsonb_array_length(task_snapshot),
      'estimatedMinutesScope','leaf_tasks',
      'estimatedMinutes',(select coalesce(sum(greatest(t.estimated_minutes,0)),0) from public.tasks t
        where t.board_id=p_board_id and t.archived_at is null
          and not exists(select 1 from public.tasks child where child.parent_task_id=t.id and child.archived_at is null)),
      'capturedAt',now()),true,(select auth.uid()),now()) returning id into baseline_id;
  update public.boards set baseline_start=planned_start,baseline_end=planned_end where id=p_board_id;
  return baseline_id;
end $$;

commit;
