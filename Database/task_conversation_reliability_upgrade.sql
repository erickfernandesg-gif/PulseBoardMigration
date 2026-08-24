-- Confiabilidade da conversa das demandas.
-- Pré-requisito: execute Database/boards_reliability_upgrade.sql primeiro.
-- Este script é idempotente e pode ser executado novamente com segurança no SQL Editor do Supabase.

begin;

alter table public.task_comments add column if not exists message_type varchar(20) default 'message';
alter table public.task_comments add column if not exists reply_to_id uuid references public.task_comments(id) on delete set null;
alter table public.task_comments add column if not exists updated_at timestamptz;
alter table public.task_comments add column if not exists deleted_at timestamptz;
create index if not exists task_comments_task_created_idx on public.task_comments(task_id,created_at);

create table if not exists public.task_comment_attachments (
  id uuid default gen_random_uuid() primary key,
  comment_id uuid references public.task_comments(id) on delete cascade not null,
  task_id uuid references public.tasks(id) on delete cascade not null,
  uploaded_by uuid references public.profiles(id) on delete restrict not null,
  storage_path text not null unique,
  file_name varchar(255) not null,
  content_type varchar(100) not null,
  file_size bigint not null check(file_size > 0 and file_size <= 8388608),
  created_at timestamptz not null default now()
);
create index if not exists task_comment_attachments_comment_idx on public.task_comment_attachments(comment_id);
create index if not exists task_comment_attachments_task_idx on public.task_comment_attachments(task_id);

create table if not exists public.task_mentions (
  id uuid default gen_random_uuid() primary key,
  task_id uuid references public.tasks(id) on delete cascade not null,
  comment_id uuid references public.task_comments(id) on delete cascade not null,
  mentioned_user_id uuid references public.profiles(id) on delete cascade not null,
  mentioned_by uuid references public.profiles(id) on delete cascade not null,
  created_at timestamptz not null default now(),
  unique(comment_id,mentioned_user_id)
);
create index if not exists task_mentions_user_idx on public.task_mentions(mentioned_user_id,created_at desc);

insert into storage.buckets(id,name,public,file_size_limit,allowed_mime_types)
values('task-chat','task-chat',false,8388608,array['image/jpeg','image/png','image/webp','image/gif'])
on conflict(id) do update set public=false,file_size_limit=excluded.file_size_limit,
  allowed_mime_types=excluded.allowed_mime_types;

alter table public.task_comments enable row level security;
alter table public.task_comment_attachments enable row level security;
alter table public.task_mentions enable row level security;

drop policy if exists comments_manage on public.task_comments;
drop policy if exists comments_select on public.task_comments;
create policy comments_select on public.task_comments for select to authenticated
  using((select private.can_read_task(task_id)));
drop policy if exists comments_insert on public.task_comments;
create policy comments_insert on public.task_comments for insert to authenticated
  with check(user_id=(select auth.uid()) and (select private.can_read_task(task_id)));
drop policy if exists comments_update_own on public.task_comments;
create policy comments_update_own on public.task_comments for update to authenticated
  using(user_id=(select auth.uid()) and deleted_at is null and (select private.can_read_task(task_id)))
  with check(user_id=(select auth.uid()) and (select private.can_read_task(task_id)));
drop policy if exists comments_delete_own on public.task_comments;
create policy comments_delete_own on public.task_comments for delete to authenticated
  using((user_id=(select auth.uid()) or public.is_manager()) and (select private.can_read_task(task_id)));

drop policy if exists comment_attachments_select on public.task_comment_attachments;
create policy comment_attachments_select on public.task_comment_attachments for select to authenticated
  using((select private.can_read_task(task_id)));
drop policy if exists comment_attachments_insert on public.task_comment_attachments;
create policy comment_attachments_insert on public.task_comment_attachments for insert to authenticated
  with check(uploaded_by=(select auth.uid()) and (select private.can_read_task(task_id)));
drop policy if exists comment_attachments_delete_own on public.task_comment_attachments;
create policy comment_attachments_delete_own on public.task_comment_attachments for delete to authenticated
  using((uploaded_by=(select auth.uid()) or public.is_manager()) and (select private.can_read_task(task_id)));

drop policy if exists mentions_read on public.task_mentions;
create policy mentions_read on public.task_mentions for select to authenticated
  using(mentioned_user_id=(select auth.uid()) or (select private.can_read_task(task_id)));
drop policy if exists mentions_insert on public.task_mentions;
create policy mentions_insert on public.task_mentions for insert to authenticated
  with check(mentioned_by=(select auth.uid()) and (select private.can_read_task(task_id)));

grant select,insert,update,delete on public.task_comments to authenticated;
grant select,insert,delete on public.task_comment_attachments to authenticated;
grant select,insert on public.task_mentions to authenticated;
grant all on public.task_comments,public.task_comment_attachments,public.task_mentions to service_role;

commit;
