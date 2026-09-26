-- Limpeza controlada de faturas canceladas e cenários DEMO.

create or replace function private.delete_cancelled_billing_invoice_impl(p_invoice_id uuid)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
  invoice public.billing_invoices%rowtype;
  actor uuid := (select auth.uid());
begin
  if actor is null then
    raise exception 'Autenticação necessária.' using errcode = '42501';
  end if;

  select * into invoice from public.billing_invoices where id = p_invoice_id for update;
  if invoice.id is null or not private.can_manage_billing_invoice(invoice.id) then
    raise exception 'Sem permissão para excluir esta fatura.' using errcode = '42501';
  end if;
  if invoice.status <> 'cancelled' then
    raise exception 'Somente faturas canceladas podem ser excluídas.' using errcode = '42501';
  end if;
  if exists (select 1 from public.time_logs where invoice_id = invoice.id) then
    raise exception 'A fatura ainda possui apontamentos vinculados. Estorne os apontamentos antes de excluí-la.' using errcode = '42501';
  end if;

  delete from public.billing_invoices where id = invoice.id;
end;
$$;

create or replace function public.delete_cancelled_billing_invoice(p_invoice_id uuid)
returns void
language sql
security definer
set search_path = ''
as $$
  select private.delete_cancelled_billing_invoice_impl(p_invoice_id);
$$;

create or replace function private.clean_demo_billing_scenario_impl(p_invoice_id uuid)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
  invoice public.billing_invoices%rowtype;
  contract public.client_contracts%rowtype;
  demo_board public.boards%rowtype;
  actor uuid := (select auth.uid());
begin
  if actor is null then
    raise exception 'Autenticação necessária.' using errcode = '42501';
  end if;

  select * into invoice from public.billing_invoices where id = p_invoice_id for update;
  if invoice.id is null or not private.can_manage_billing_invoice(invoice.id) then
    raise exception 'Sem permissão para limpar este cenário.' using errcode = '42501';
  end if;
  if invoice.status <> 'cancelled' or invoice.reference not like 'DEMO-%' then
    raise exception 'A limpeza total é permitida somente para faturas DEMO canceladas.' using errcode = '42501';
  end if;
  if exists (select 1 from public.time_logs where invoice_id = invoice.id) then
    raise exception 'A fatura DEMO ainda possui apontamentos vinculados.' using errcode = '42501';
  end if;

  select * into contract from public.client_contracts where id = invoice.contract_id for update;
  if contract.id is null or contract.name not like 'Contrato DEMO%' then
    raise exception 'O contrato vinculado não é um contrato DEMO elegível para limpeza.' using errcode = '42501';
  end if;
  if not exists (select 1 from public.clients where id = contract.client_id and name like 'Cliente Demonstração%') then
    raise exception 'O cliente vinculado não é um cliente DEMO elegível para limpeza.' using errcode = '42501';
  end if;
  if exists (select 1 from public.billing_invoices where client_id = contract.client_id and id <> invoice.id) then
    raise exception 'O cliente DEMO possui outra fatura e não pode ser removido em lote.' using errcode = '42501';
  end if;
  if exists (select 1 from public.client_contracts where client_id = contract.client_id and id <> contract.id and name not like 'Contrato DEMO%') then
    raise exception 'O cliente DEMO possui contrato não demonstrativo e não pode ser removido em lote.' using errcode = '42501';
  end if;
  if exists (
    select 1 from public.tasks task
    left join public.boards board on board.id = task.board_id
    where task.client_id = contract.client_id
      and (board.id is null or board.name not like 'DEMO —%')
  ) then
    raise exception 'O cliente DEMO possui tarefas fora de projetos DEMO e não pode ser removido em lote.' using errcode = '42501';
  end if;

  if contract.board_id is not null then
    select * into demo_board from public.boards where id = contract.board_id for update;
    if demo_board.id is null or demo_board.name not like 'DEMO —%' then
      raise exception 'O projeto vinculado não é um projeto DEMO elegível para limpeza.' using errcode = '42501';
    end if;
  end if;

  delete from public.billing_invoices where id = invoice.id;
  if demo_board.id is not null then
    delete from public.boards where id = demo_board.id;
  end if;
  delete from public.clients where id = contract.client_id;
end;
$$;

create or replace function public.clean_demo_billing_scenario(p_invoice_id uuid)
returns void
language sql
security definer
set search_path = ''
as $$
  select private.clean_demo_billing_scenario_impl(p_invoice_id);
$$;

revoke execute on function private.delete_cancelled_billing_invoice_impl(uuid), public.delete_cancelled_billing_invoice(uuid), private.clean_demo_billing_scenario_impl(uuid), public.clean_demo_billing_scenario(uuid) from public, anon;
grant execute on function public.delete_cancelled_billing_invoice(uuid), public.clean_demo_billing_scenario(uuid) to authenticated, service_role;
