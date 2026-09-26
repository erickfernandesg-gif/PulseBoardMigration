-- Uma única regra protege todas as origens de notificações do aplicativo.
create or replace function private.filter_notification_by_preference()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  recipient uuid := coalesce(new.recipient_id, new.user_id);
  preference public.notification_preferences%rowtype;
begin
  if recipient is null then return new; end if;
  select * into preference from public.notification_preferences where user_id = recipient;
  if preference.user_id is not null and not preference.in_app then return null; end if;
  if new.type = 'mention' and preference.user_id is not null and not preference.mention_alerts then return null; end if;
  return new;
end;
$$;

drop trigger if exists filter_notification_by_preference on public.notifications;
create trigger filter_notification_by_preference
before insert on public.notifications
for each row execute function private.filter_notification_by_preference();
