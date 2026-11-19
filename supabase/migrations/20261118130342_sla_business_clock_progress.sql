-- Usa intervalos exatos: arredondar o último minuto para zero travava a transação.
create or replace function private.business_due(p_start timestamptz,p_minutes integer,p_team_id uuid)
returns timestamptz language plpgsql stable set search_path='' as $$
declare
 cursor_local timestamp := coalesce(p_start,now()) at time zone 'America/Sao_Paulo';
 remaining interval := make_interval(mins=>greatest(coalesce(p_minutes,0),0));
 available interval;
 part interval;
begin
 if remaining=interval '0' then return coalesce(p_start,now()); end if;
 loop
  if cursor_local::time<time '09:00' then cursor_local:=cursor_local::date+time '09:00'; end if;
  if extract(isodow from cursor_local)>=6 or cursor_local::time>=time '18:00'
    or exists(select 1 from public.company_holidays h where h.holiday_date=cursor_local::date and(h.team_id is null or h.team_id=p_team_id)) then
   cursor_local:=(cursor_local::date+1)+time '09:00'; continue;
  end if;
  available:=(cursor_local::date+time '18:00')-cursor_local;
  part:=least(remaining,available);
  cursor_local:=cursor_local+part;
  remaining:=remaining-part;
  exit when remaining<=interval '0';
 end loop;
 return cursor_local at time zone 'America/Sao_Paulo';
end $$;
