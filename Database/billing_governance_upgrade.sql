-- Governanca financeira: execute depois de planning_governance_scope_fix.sql.
-- A cobranca automatica passa a ser sempre por projeto + cliente + periodo.

begin;

do $$
begin
  if not exists(select 1 from pg_constraint where conname='client_contracts_dates_check' and conrelid='public.client_contracts'::regclass) then
    alter table public.client_contracts add constraint client_contracts_dates_check check (ends_on is null or ends_on >= starts_on) not valid;
  end if;
  if not exists(select 1 from pg_constraint where conname='client_contracts_included_minutes_check' and conrelid='public.client_contracts'::regclass) then
    alter table public.client_contracts add constraint client_contracts_included_minutes_check check (included_minutes is null or included_minutes >= 0) not valid;
  end if;
end $$;

alter table public.billing_invoices add column if not exists status_changed_by uuid references public.profiles(id) on delete set null;
alter table public.billing_invoices add column if not exists status_changed_at timestamptz;
alter table public.billing_invoices add column if not exists cancelled_at timestamptz;

create table if not exists public.billing_invoice_events (
  id uuid default extensions.uuid_generate_v4() primary key,
  invoice_id uuid references public.billing_invoices(id) on delete cascade not null,
  actor_id uuid references public.profiles(id) on delete set null,
  event_type varchar(40) not null check(event_type in ('created','issued','paid','cancelled')),
  details jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now()
);
create index if not exists billing_invoice_events_invoice_idx on public.billing_invoice_events(invoice_id,created_at desc);
alter table public.billing_invoice_events enable row level security;
revoke all on public.billing_invoice_events from anon;
grant select on public.billing_invoice_events to authenticated;

-- A taxa historica nunca e aceita do cliente. O banco sempre a calcula com o
-- contrato vigente do proprio projeto e cliente na data do apontamento.
create or replace function public.snapshot_time_log_rates()
returns trigger language plpgsql security definer set search_path=public as $$
declare target public.tasks%rowtype; contract_rate numeric(12,2);
begin
  select * into target from public.tasks where id=new.task_id;
  if target.id is null then raise exception 'Tarefa do apontamento não encontrada.'; end if;
  select coalesce(hourly_rate,0) into new.cost_rate_snapshot from public.user_rates where user_id=new.user_id;
  new.cost_rate_snapshot := coalesce(new.cost_rate_snapshot,0);
  if new.is_billable then
    select billing_rate into contract_rate from public.client_contracts
      where is_active and contract_type='hourly' and client_id=target.client_id and board_id=target.board_id
        and starts_on<=new.log_date and (ends_on is null or ends_on>=new.log_date)
      order by starts_on desc,created_at desc limit 1;
    new.billing_rate_snapshot := coalesce(contract_rate,0);
  else
    new.billing_rate_snapshot := 0;
  end if;
  new.approval_status := 'pending';
  new.billing_status := 'unbilled';
  new.approved_by := null;
  new.approved_at := null;
  new.invoice_id := null;
  return new;
end $$;

create or replace function private.protect_time_log_financial_fields()
returns trigger language plpgsql security definer set search_path='' as $$
declare operation text := current_setting('app.billing_operation', true);
begin
  if old.billing_status <> 'unbilled' and operation is distinct from 'cancel_draft' then
    raise exception 'Apontamentos faturados são imutáveis.' using errcode='42501';
  end if;
  if operation is distinct from 'billing_review' and operation is distinct from 'cancel_draft' and operation is distinct from 'invoice' then
    if new.approval_status is distinct from old.approval_status
       or new.approved_by is distinct from old.approved_by
       or new.approved_at is distinct from old.approved_at
       or new.billing_status is distinct from old.billing_status
       or new.invoice_id is distinct from old.invoice_id
       or new.cost_rate_snapshot is distinct from old.cost_rate_snapshot
       or new.billing_rate_snapshot is distinct from old.billing_rate_snapshot then
      raise exception 'Campos financeiros do apontamento só podem ser alterados pelo fluxo de faturamento.' using errcode='42501';
    end if;
  end if;
  return new;
end $$;
drop trigger if exists protect_time_log_financial_fields on public.time_logs;
create trigger protect_time_log_financial_fields before update on public.time_logs
for each row execute function private.protect_time_log_financial_fields();

create or replace function private.can_read_billing_invoice(target_invoice_id uuid)
returns boolean language sql stable security definer set search_path='' as $$
  select (select public.is_admin()) or exists(
    select 1 from public.billing_invoice_items i
    join public.time_logs l on l.id=i.time_log_id
    join public.tasks t on t.id=l.task_id
    where i.invoice_id=target_invoice_id and private.can_read_board(t.board_id)
  );
$$;
create or replace function private.can_manage_billing_invoice(target_invoice_id uuid)
returns boolean language sql stable security definer set search_path='' as $$
  select (select public.is_admin()) or (
    (select public.is_manager()) and exists(select 1 from public.billing_invoice_items where invoice_id=target_invoice_id)
    and not exists(
      select 1 from public.billing_invoice_items i
      join public.time_logs l on l.id=i.time_log_id
      join public.tasks t on t.id=l.task_id
      where i.invoice_id=target_invoice_id and not private.can_edit_board(t.board_id)
    )
  );
$$;
revoke execute on function private.protect_time_log_financial_fields(), private.can_read_billing_invoice(uuid), private.can_manage_billing_invoice(uuid) from public,anon;
grant execute on function private.can_read_billing_invoice(uuid), private.can_manage_billing_invoice(uuid) to authenticated;

drop policy if exists contracts_read on public.client_contracts;
drop policy if exists contracts_manage on public.client_contracts;
drop policy if exists contracts_insert on public.client_contracts;
drop policy if exists contracts_update on public.client_contracts;
drop policy if exists contracts_delete on public.client_contracts;
create policy contracts_read on public.client_contracts for select to authenticated using(
  (board_id is not null and (select private.can_read_board(board_id))) or (board_id is null and (select public.is_admin()))
);
create policy contracts_insert on public.client_contracts for insert to authenticated with check(
  contract_type='hourly' and board_id is not null and (select private.can_edit_board(board_id))
);
create policy contracts_update on public.client_contracts for update to authenticated
  using(board_id is not null and (select private.can_edit_board(board_id)))
  with check(contract_type='hourly' and board_id is not null and (select private.can_edit_board(board_id)));
create policy contracts_delete on public.client_contracts for delete to authenticated
  using(board_id is not null and (select private.can_edit_board(board_id)));
revoke all on public.client_contracts from anon;
grant select,insert,update,delete on public.client_contracts to authenticated;

drop policy if exists invoices_read on public.billing_invoices;
drop policy if exists invoices_manage on public.billing_invoices;
drop policy if exists invoices_insert on public.billing_invoices;
drop policy if exists invoices_update on public.billing_invoices;
drop policy if exists invoices_delete on public.billing_invoices;
create policy invoices_read on public.billing_invoices for select to authenticated using((select private.can_read_billing_invoice(id)));
create policy invoices_update_blocked on public.billing_invoices for update to authenticated using(false) with check(false);
drop policy if exists invoice_items_read on public.billing_invoice_items;
drop policy if exists invoice_items_manage on public.billing_invoice_items;
drop policy if exists invoice_items_insert on public.billing_invoice_items;
drop policy if exists invoice_items_update on public.billing_invoice_items;
drop policy if exists invoice_items_delete on public.billing_invoice_items;
create policy invoice_items_read on public.billing_invoice_items for select to authenticated using((select private.can_read_billing_invoice(invoice_id)));
revoke all on public.billing_invoices, public.billing_invoice_items from anon;
revoke insert,update,delete on public.billing_invoices, public.billing_invoice_items from authenticated;
grant select on public.billing_invoices, public.billing_invoice_items to authenticated;
drop policy if exists billing_invoice_events_read on public.billing_invoice_events;
create policy billing_invoice_events_read on public.billing_invoice_events for select to authenticated using((select private.can_read_billing_invoice(invoice_id)));

create or replace function private.review_billing_time_log_impl(p_log_id uuid,p_approve boolean,p_reviewer_id uuid)
returns void language plpgsql security definer set search_path='' as $$
declare target public.time_logs%rowtype; task_board uuid; actor uuid := (select auth.uid());
begin
  if actor is null or actor<>p_reviewer_id then raise exception 'Revisor inválido.' using errcode='42501'; end if;
  select l.* into target from public.time_logs l where l.id=p_log_id for update;
  select t.board_id into task_board from public.tasks t where t.id=target.task_id;
  if target.id is null or not private.can_edit_board(task_board) then raise exception 'Sem permissão para aprovar este apontamento.' using errcode='42501'; end if;
  if target.approval_status<>'pending' or target.billing_status<>'unbilled' then raise exception 'Somente apontamentos pendentes e não faturados podem ser revisados.'; end if;
  perform set_config('app.billing_operation','billing_review',true);
  update public.time_logs set approval_status=case when p_approve then 'approved' else 'rejected' end,approved_by=actor,approved_at=now() where id=target.id;
  insert into public.activity_log(task_id,board_id,user_id,action,details)
  values(target.task_id,task_board,actor,'billing_time_review',jsonb_build_object('decision',case when p_approve then 'approved' else 'rejected' end));
end $$;
create or replace function public.review_billing_time_log(p_log_id uuid,p_approve boolean,p_reviewer_id uuid)
returns void language sql security definer set search_path='' as $$ select private.review_billing_time_log_impl(p_log_id,p_approve,p_reviewer_id); $$;
revoke execute on function private.review_billing_time_log_impl(uuid,boolean,uuid), public.review_billing_time_log(uuid,boolean,uuid) from public,anon;
grant execute on function public.review_billing_time_log(uuid,boolean,uuid) to authenticated;

create or replace function private.delete_pending_billing_time_log_impl(p_log_id uuid,p_requester_id uuid)
returns void language plpgsql security definer set search_path='' as $$
declare target public.time_logs%rowtype; task_board uuid; actor uuid := (select auth.uid());
begin
  if actor is null or actor<>p_requester_id then raise exception 'Solicitante inválido.' using errcode='42501'; end if;
  select l.* into target from public.time_logs l where l.id=p_log_id for update;
  select t.board_id into task_board from public.tasks t where t.id=target.task_id;
  if target.id is null or not private.can_edit_board(task_board) then raise exception 'Sem permissão para excluir este apontamento.' using errcode='42501'; end if;
  if target.approval_status<>'pending' or target.billing_status<>'unbilled' then raise exception 'Somente apontamentos pendentes e não faturados podem ser excluídos.' using errcode='42501'; end if;
  insert into public.activity_log(task_id,board_id,user_id,action,details)
  values(target.task_id,task_board,actor,'billing_time_log_deleted',jsonb_build_object('minutes',target.minutes,'is_billable',target.is_billable));
  delete from public.time_logs where id=target.id;
end $$;
create or replace function public.delete_pending_billing_time_log(p_log_id uuid,p_requester_id uuid)
returns void language sql security definer set search_path='' as $$ select private.delete_pending_billing_time_log_impl(p_log_id,p_requester_id); $$;
revoke execute on function private.delete_pending_billing_time_log_impl(uuid,uuid), public.delete_pending_billing_time_log(uuid,uuid) from public,anon;
grant execute on function public.delete_pending_billing_time_log(uuid,uuid) to authenticated;
drop policy if exists time_logs_delete on public.time_logs;
create policy time_logs_delete on public.time_logs for delete to authenticated using(
  user_id=(select auth.uid()) and approval_status='pending' and billing_status='unbilled');

drop function if exists public.generate_billing_invoice(uuid,uuid,date,date,date);
create or replace function private.generate_billing_invoice_impl(
  p_client_id uuid,p_board_id uuid,p_creator_id uuid,p_period_start date,p_period_end date,p_due_date date default null)
returns uuid language plpgsql security definer set search_path='' as $$
declare v_invoice_id uuid; v_reference text; v_total numeric(14,2); v_contract_id uuid; actor uuid := (select auth.uid());
begin
  if actor is null or actor<>p_creator_id or not private.can_edit_board(p_board_id) then raise exception 'Acesso financeiro negado.' using errcode='42501'; end if;
  if p_period_end<p_period_start then raise exception 'Período de faturamento inválido.'; end if;
  if p_due_date is not null and p_due_date<p_period_end then raise exception 'Vencimento não pode ser anterior ao fim do período.'; end if;
  perform pg_advisory_xact_lock(hashtext('invoice:'||p_client_id::text||':'||p_board_id::text||':'||p_period_start::text||':'||p_period_end::text));
  select id into v_contract_id from public.client_contracts where client_id=p_client_id and board_id=p_board_id
    and is_active and contract_type='hourly' and starts_on<=p_period_end and (ends_on is null or ends_on>=p_period_start)
    order by starts_on desc,created_at desc limit 1;
  if v_contract_id is null then raise exception 'Não existe contrato por hora ativo para este projeto, cliente e período.'; end if;
  with locked_logs as materialized(
    select l.id,l.minutes,l.billing_rate_snapshot from public.time_logs l join public.tasks t on t.id=l.task_id
    where t.client_id=p_client_id and t.board_id=p_board_id and l.is_billable and l.approval_status='approved'
      and l.billing_status='unbilled' and l.log_date between p_period_start and p_period_end for update of l)
  select coalesce(sum(billing_rate_snapshot*minutes/60.0),0) into v_total from locked_logs;
  if v_total<=0 then raise exception 'Não existem horas aprovadas, valorizadas e não faturadas neste período.'; end if;
  v_invoice_id:=extensions.uuid_generate_v4();
  v_reference:='PB-'||to_char(now(),'YYYYMMDD')||'-'||upper(substr(replace(v_invoice_id::text,'-',''),1,6));
  insert into public.billing_invoices(id,client_id,contract_id,reference,status,period_start,period_end,due_date,subtotal,total,created_by,status_changed_by,status_changed_at)
  values(v_invoice_id,p_client_id,v_contract_id,v_reference,'draft',p_period_start,p_period_end,p_due_date,v_total,v_total,actor,actor,now());
  insert into public.billing_invoice_items(invoice_id,time_log_id,description,minutes,unit_rate,amount)
    select v_invoice_id,l.id,t.title||' - '||to_char(l.log_date,'DD/MM/YYYY'),l.minutes,l.billing_rate_snapshot,l.billing_rate_snapshot*l.minutes/60.0
    from public.time_logs l join public.tasks t on t.id=l.task_id
    where t.client_id=p_client_id and t.board_id=p_board_id and l.is_billable and l.approval_status='approved' and l.billing_status='unbilled' and l.log_date between p_period_start and p_period_end;
  perform set_config('app.billing_operation','invoice',true);
  update public.time_logs l set billing_status='invoiced',invoice_id=v_invoice_id from public.tasks t
    where t.id=l.task_id and t.client_id=p_client_id and t.board_id=p_board_id and l.is_billable and l.approval_status='approved' and l.billing_status='unbilled' and l.log_date between p_period_start and p_period_end;
  insert into public.billing_invoice_events(invoice_id,actor_id,event_type,details) values(v_invoice_id,actor,'created',jsonb_build_object('board_id',p_board_id,'total',v_total));
  return v_invoice_id;
end $$;
create or replace function public.generate_billing_invoice(
  p_client_id uuid,p_board_id uuid,p_creator_id uuid,p_period_start date,p_period_end date,p_due_date date default null)
returns uuid language sql security invoker set search_path='' as $$ select private.generate_billing_invoice_impl(p_client_id,p_board_id,p_creator_id,p_period_start,p_period_end,p_due_date); $$;
revoke execute on function private.generate_billing_invoice_impl(uuid,uuid,uuid,date,date,date), public.generate_billing_invoice(uuid,uuid,uuid,date,date,date) from public,anon;
grant execute on function public.generate_billing_invoice(uuid,uuid,uuid,date,date,date) to authenticated;

create or replace function private.update_billing_invoice_status_impl(p_invoice_id uuid,p_status text)
returns void language plpgsql security definer set search_path='' as $$
declare invoice public.billing_invoices%rowtype; actor uuid := (select auth.uid()); event_name text;
begin
  if actor is null then raise exception 'Autenticação necessária.' using errcode='42501'; end if;
  select * into invoice from public.billing_invoices where id=p_invoice_id for update;
  if invoice.id is null or not private.can_manage_billing_invoice(invoice.id) then raise exception 'Sem permissão para alterar esta fatura.' using errcode='42501'; end if;
  if invoice.status='draft' and p_status='issued' then event_name:='issued';
  elsif invoice.status='draft' and p_status='cancelled' then event_name:='cancelled';
  elsif invoice.status='issued' and p_status='paid' then event_name:='paid';
  else raise exception 'Transição de situação inválida. Rascunho pode ser emitido ou cancelado; fatura emitida pode ser marcada como paga.'; end if;
  if p_status='cancelled' then
    perform set_config('app.billing_operation','cancel_draft',true);
    update public.time_logs l set billing_status='unbilled',invoice_id=null
      from public.billing_invoice_items i where i.invoice_id=invoice.id and i.time_log_id=l.id and l.invoice_id=invoice.id;
  end if;
  update public.billing_invoices set status=p_status,status_changed_by=actor,status_changed_at=now(),cancelled_at=case when p_status='cancelled' then now() else cancelled_at end where id=invoice.id;
  insert into public.billing_invoice_events(invoice_id,actor_id,event_type,details) values(invoice.id,actor,event_name,jsonb_build_object('from_status',invoice.status,'to_status',p_status));
end $$;
create or replace function public.update_billing_invoice_status(p_invoice_id uuid,p_status text)
returns void language sql security invoker set search_path='' as $$ select private.update_billing_invoice_status_impl(p_invoice_id,p_status); $$;
revoke execute on function private.update_billing_invoice_status_impl(uuid,text), public.update_billing_invoice_status(uuid,text) from public,anon;
grant execute on function public.update_billing_invoice_status(uuid,text) to authenticated;

commit;
