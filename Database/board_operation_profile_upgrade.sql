-- Perfil operacional controla visibilidade e regras, sem apagar apontamentos ou financeiro.
begin;

alter table public.boards add column if not exists operation_profile text not null default 'delivery';
alter table public.boards drop constraint if exists boards_operation_profile_check;
alter table public.boards add constraint boards_operation_profile_check
  check(operation_profile in ('delivery','service','internal'));

-- Preserva o comportamento existente: boards que já usam SLA passam a ser operação;
-- os demais continuam como projetos/entregas.
update public.boards board set operation_profile = 'service'
where operation_profile='delivery' and exists (
  select 1 from public.tasks task where task.board_id=board.id and coalesce(task.sla_minutes,0)>0
);

create or replace function private.clear_sla_outside_service_profile()
returns trigger language plpgsql security definer set search_path='' as $$
begin
  if new.operation_profile <> 'service' and old.operation_profile='service' then
    update public.tasks set sla_minutes=null,sla_due_at=null,sla_level=null
    where board_id=new.id and (sla_minutes is not null or sla_due_at is not null or sla_level is not null);
  end if;
  return new;
end $$;
drop trigger if exists clear_sla_outside_service_profile on public.boards;
create trigger clear_sla_outside_service_profile after update of operation_profile on public.boards
for each row execute function private.clear_sla_outside_service_profile();

commit;
