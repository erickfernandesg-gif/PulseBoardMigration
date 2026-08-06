create extension if not exists "uuid-ossp";

begin;

-- Correção compatível tanto com banco novo quanto com uma tabela notifications antiga.
create table if not exists public.notifications (
  id uuid default uuid_generate_v4() primary key,
  recipient_id uuid references public.profiles(id) on delete cascade,
  actor_id uuid references public.profiles(id) on delete set null,
  task_id uuid references public.tasks(id) on delete cascade,
  board_id uuid references public.boards(id) on delete cascade,
  type varchar(60),
  title varchar(255),
  message text,
  action_url text,
  priority varchar(20) default 'normal',
  deduplication_key text,
  read_at timestamptz,
  archived_at timestamptz,
  created_at timestamptz default now()
);

alter table public.notifications add column if not exists recipient_id uuid references public.profiles(id) on delete cascade;
alter table public.notifications add column if not exists actor_id uuid references public.profiles(id) on delete set null;
alter table public.notifications add column if not exists task_id uuid references public.tasks(id) on delete cascade;
alter table public.notifications add column if not exists board_id uuid references public.boards(id) on delete cascade;
alter table public.notifications add column if not exists type varchar(60);
alter table public.notifications add column if not exists title varchar(255);
alter table public.notifications add column if not exists message text;
alter table public.notifications add column if not exists action_url text;
alter table public.notifications add column if not exists priority varchar(20) default 'normal';
alter table public.notifications add column if not exists deduplication_key text;
alter table public.notifications add column if not exists read_at timestamptz;
alter table public.notifications add column if not exists archived_at timestamptz;
alter table public.notifications add column if not exists created_at timestamptz default now();

-- Interrompe antes do índice com uma mensagem clara se a coluna não estiver disponível.
do $$
begin
  if not exists (
    select 1
    from information_schema.columns
    where table_schema = 'public'
      and table_name = 'notifications'
      and column_name = 'deduplication_key'
  ) then
    raise exception 'A coluna public.notifications.deduplication_key não foi criada';
  end if;
end
$$;

create unique index if not exists notifications_deduplication_key_idx
  on public.notifications (deduplication_key);

create index if not exists notifications_recipient_unread_idx
  on public.notifications (recipient_id, created_at desc)
  where read_at is null;

commit;

-- Deve retornar uma linha com column_name = deduplication_key e data_type = text.
select column_name, data_type, is_nullable
from information_schema.columns
where table_schema = 'public'
  and table_name = 'notifications'
  and column_name = 'deduplication_key';
