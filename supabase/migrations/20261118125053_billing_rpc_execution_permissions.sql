-- Os wrappers SECURITY INVOKER precisam executar suas implementações privadas.
-- As implementações continuam conferindo ator, perfil e escopo do projeto.
do $$ declare definition text; begin
 definition:=pg_get_functiondef('private.generate_billing_invoice_impl(uuid,uuid,uuid,date,date,date)'::regprocedure);
 execute replace(definition,'if actor is null or actor<>p_creator_id','if actor is null or not public.is_manager() or actor<>p_creator_id');
 definition:=pg_get_functiondef('private.update_billing_invoice_status_impl(uuid,text)'::regprocedure);
 execute replace(definition,'if actor is null then','if actor is null or not public.is_manager() then');
end $$;
revoke all on function private.generate_billing_invoice_impl(uuid,uuid,uuid,date,date,date),private.update_billing_invoice_status_impl(uuid,text) from public,anon;
grant execute on function private.generate_billing_invoice_impl(uuid,uuid,uuid,date,date,date),private.update_billing_invoice_status_impl(uuid,text) to authenticated;
