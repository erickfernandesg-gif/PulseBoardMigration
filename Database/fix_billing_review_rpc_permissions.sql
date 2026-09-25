-- Permite que a RPC pública de revisão de apontamentos invoque a implementação
-- privada sem expor esta implementação a usuários autenticados.
-- As validações de auth.uid(), gestor do board e estado do apontamento permanecem
-- em private.review_billing_time_log_impl.

create or replace function public.review_billing_time_log(p_log_id uuid, p_approve boolean, p_reviewer_id uuid)
returns void
language sql
security definer
set search_path = ''
as $$
  select private.review_billing_time_log_impl(p_log_id, p_approve, p_reviewer_id);
$$;

revoke execute on function public.review_billing_time_log(uuid, boolean, uuid) from public, anon;
grant execute on function public.review_billing_time_log(uuid, boolean, uuid) to authenticated, service_role;
