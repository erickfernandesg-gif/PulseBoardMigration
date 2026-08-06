-- Instalação isolada do chat de atividades do PulseBoard.
-- Execute todo este arquivo no SQL Editor do Supabase, sem selecionar apenas um trecho.

begin;

create extension if not exists "uuid-ossp";

alter table public.task_comments add column if not exists message_type varchar(20) default 'message';
alter table public.task_comments add column if not exists reply_to_id uuid references public.task_comments(id) on delete set null;
alter table public.task_comments add column if not exists updated_at timestamptz;
alter table public.task_comments add column if not exists deleted_at timestamptz;
create index if not exists task_comments_task_created_idx on public.task_comments(task_id,created_at);

create table if not exists public.task_comment_attachments (
  id uuid default uuid_generate_v4() primary key,
  comment_id uuid references public.task_comments(id) on delete cascade not null,
  task_id uuid references public.tasks(id) on delete cascade not null,
  uploaded_by uuid references public.profiles(id) on delete restrict not null,
  storage_path text not null unique,
  file_name varchar(255) not null,
  content_type varchar(100) not null,
  file_size bigint not null check(file_size > 0 and file_size <= 8388608),
  created_at timestamptz not null default now()
);

create index if not exists task_comment_attachments_comment_idx
  on public.task_comment_attachments(comment_id);
create index if not exists task_comment_attachments_task_idx
  on public.task_comment_attachments(task_id);

insert into storage.buckets(id,name,public,file_size_limit,allowed_mime_types)
values('task-chat','task-chat',false,8388608,array['image/jpeg','image/png','image/webp','image/gif'])
on conflict(id) do update set
  public=false,
  file_size_limit=excluded.file_size_limit,
  allowed_mime_types=excluded.allowed_mime_types;

alter table public.task_comments enable row level security;
alter table public.task_comment_attachments enable row level security;

drop policy if exists comments_manage on public.task_comments;
drop policy if exists comments_select on public.task_comments;
create policy comments_select on public.task_comments for select to authenticated using(
  exists(select 1 from public.tasks visible_task where visible_task.id=task_comments.task_id)
);
drop policy if exists comments_insert on public.task_comments;
create policy comments_insert on public.task_comments for insert to authenticated with check(
  user_id=(select auth.uid())
  and exists(select 1 from public.tasks visible_task where visible_task.id=task_comments.task_id)
);
drop policy if exists comments_update_own on public.task_comments;
create policy comments_update_own on public.task_comments for update to authenticated
using(user_id=(select auth.uid()) and deleted_at is null)
with check(user_id=(select auth.uid()));
drop policy if exists comments_delete_own on public.task_comments;
create policy comments_delete_own on public.task_comments for delete to authenticated
using(user_id=(select auth.uid()) or public.is_manager());

drop policy if exists comment_attachments_select on public.task_comment_attachments;
create policy comment_attachments_select on public.task_comment_attachments for select to authenticated using(
  exists(select 1 from public.tasks visible_task where visible_task.id=task_comment_attachments.task_id)
);
drop policy if exists comment_attachments_insert on public.task_comment_attachments;
create policy comment_attachments_insert on public.task_comment_attachments for insert to authenticated with check(
  uploaded_by=(select auth.uid())
  and exists(select 1 from public.tasks visible_task where visible_task.id=task_comment_attachments.task_id)
);
drop policy if exists comment_attachments_delete_own on public.task_comment_attachments;
create policy comment_attachments_delete_own on public.task_comment_attachments for delete to authenticated
using(uploaded_by=(select auth.uid()) or public.is_manager());

grant select,insert,update,delete on public.task_comments to authenticated;
grant select,insert,delete on public.task_comment_attachments to authenticated;
grant all on public.task_comments,public.task_comment_attachments to service_role;

create or replace function public.log_task_comment_change()
returns trigger language plpgsql security invoker set search_path=public as $$
declare target_board uuid;
begin
  select board_id into target_board from public.tasks where id=new.task_id;
  insert into public.activity_log(task_id,board_id,user_id,action,details)
  values(new.task_id,target_board,(select auth.uid()),
    case when new.deleted_at is distinct from old.deleted_at then 'comment_deleted' else 'comment_edited' end,
    jsonb_build_object('comment_id',new.id));
  return new;
end $$;
drop trigger if exists task_comment_change_audit on public.task_comments;
create trigger task_comment_change_audit
after update of content,deleted_at on public.task_comments
for each row execute function public.log_task_comment_change();

create or replace function public.return_task_with_question(
  p_task_id uuid,
  p_to_user_id uuid,
  p_question text
)
returns uuid language plpgsql security invoker set search_path=public as $$
declare current_task public.tasks%rowtype; comment_id uuid;
begin
  if p_to_user_id is null or nullif(trim(p_question),'') is null then
    raise exception 'Informe a pessoa e descreva a dúvida';
  end if;
  select * into current_task from public.tasks where id=p_task_id for update;
  if current_task.id is null then raise exception 'Tarefa não encontrada'; end if;
  if current_task.assigned_to is distinct from (select auth.uid()) and not public.is_manager() then
    raise exception 'Somente o executor atual ou um gestor pode devolver a ação';
  end if;
  if p_to_user_id=current_task.assigned_to then raise exception 'Selecione outra pessoa para responder'; end if;

  insert into public.task_comments(task_id,user_id,content,message_type,created_at)
  values(p_task_id,(select auth.uid()),trim(p_question),'question',now()) returning id into comment_id;
  perform public.handoff_task(
    p_task_id,p_to_user_id,current_task.status,current_task.due_date,current_task.estimated_minutes,
    p_question,null,false,null
  );
  return comment_id;
end $$;

revoke execute on function public.return_task_with_question(uuid,uuid,text) from public,anon;
grant execute on function public.return_task_with_question(uuid,uuid,text) to authenticated;

commit;

notify pgrst, 'reload schema';

-- A consulta deve retornar uma linha após a instalação.
select table_schema,table_name
from information_schema.tables
where table_schema='public' and table_name='task_comment_attachments';
