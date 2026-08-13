-- Endurecimento pós-auditoria: nenhuma função da API pública fica disponível para usuários anônimos.
do $$
declare fn record;
begin
  for fn in
    select p.oid, n.nspname, p.proname, pg_get_function_identity_arguments(p.oid) args
    from pg_proc p join pg_namespace n on n.oid = p.pronamespace
    where n.nspname = 'public'
  loop
    execute format('revoke execute on function %I.%I(%s) from public, anon, authenticated, service_role', fn.nspname, fn.proname, fn.args);
    execute format('alter function %I.%I(%s) set search_path = public, pg_temp', fn.nspname, fn.proname, fn.args);
  end loop;
end $$;

grant execute on function public.is_admin() to authenticated, service_role;
grant execute on function public.is_manager() to authenticated, service_role;
grant execute on function public.current_team_id() to authenticated, service_role;
grant execute on function public.can_manage_user(uuid) to authenticated, service_role;
grant execute on function public.ensure_due_notifications() to authenticated, service_role;
grant execute on function public.handoff_task(uuid,uuid,text,timestamptz,integer,text,text,boolean,uuid) to authenticated, service_role;
grant execute on function public.respond_task_assignment(uuid,text,text) to authenticated, service_role;
grant execute on function public.return_task_with_question(uuid,uuid,text) to authenticated, service_role;
grant execute on function public.review_task(uuid,text,text) to authenticated, service_role;
grant execute on function public.search_workspace(text,integer) to authenticated, service_role;
grant execute on function public.generate_billing_invoice(uuid,uuid,date,date,date) to authenticated, service_role;
