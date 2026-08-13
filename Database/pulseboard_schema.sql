-- PulseBoard ASP.NET Core MVC
-- Execute este arquivo no SQL Editor do Supabase. Ele é idempotente.

create extension if not exists "uuid-ossp";
create extension if not exists "pgcrypto";

create table if not exists public.teams (
  id uuid default uuid_generate_v4() primary key,
  name text not null unique,
  description text,
  created_at timestamptz default now()
);

create table if not exists public.profiles (
  id uuid references auth.users(id) on delete cascade primary key,
  email text not null,
  full_name text,
  role text not null default 'user' check (role in ('user','manager','admin')),
  avatar_url text,
  team_id uuid references public.teams(id) on delete set null,
  last_read_notifications_at timestamptz default now(),
  created_at timestamptz default now()
);

create table if not exists public.user_rates (
  user_id uuid references public.profiles(id) on delete cascade primary key,
  hourly_rate numeric(10,2) not null default 0 check (hourly_rate >= 0),
  updated_at timestamptz default now()
);

create table if not exists public.clients (
  id uuid default uuid_generate_v4() primary key,
  name text not null,
  email text,
  phone text,
  created_at timestamptz default now()
);

create table if not exists public.boards (
  id uuid default uuid_generate_v4() primary key,
  name text not null,
  description text,
  status text not null default 'active' check (status in ('active','paused','archived')),
  owner_id uuid references public.profiles(id) not null,
  settings jsonb not null default '[
    {"id":"backlog","title":"Caixa de Entrada"},
    {"id":"todo","title":"A Fazer"},
    {"id":"in-progress","title":"Em Execução"},
    {"id":"homologation","title":"Homologação"},
    {"id":"done","title":"Concluído"}
  ]'::jsonb,
  created_at timestamptz default now()
);

create table if not exists public.tasks (
  id uuid default uuid_generate_v4() primary key,
  board_id uuid references public.boards(id) on delete cascade not null,
  title text not null,
  description text,
  status text not null default 'todo',
  priority text not null default 'medium' check (priority in ('low','medium','high','critical')),
  due_date timestamptz,
  start_date timestamptz,
  completed_at timestamptz,
  assigned_to uuid references public.profiles(id) on delete set null,
  position_index integer not null default 0,
  total_minutes_spent integer not null default 0,
  target_month varchar(7),
  is_blocked boolean not null default false,
  blocker_reason text,
  client_id uuid references public.clients(id) on delete set null,
  estimated_minutes integer not null default 0,
  created_at timestamptz default now(),
  updated_at timestamptz default now()
);

alter table public.tasks add column if not exists completed_at timestamptz;
alter table public.tasks add column if not exists assigned_to uuid references public.profiles(id) on delete set null;
alter table public.tasks add column if not exists position_index integer not null default 0;
alter table public.tasks add column if not exists total_minutes_spent integer not null default 0;
alter table public.tasks add column if not exists target_month varchar(7);
alter table public.tasks add column if not exists is_blocked boolean not null default false;
alter table public.tasks add column if not exists blocker_reason text;
alter table public.tasks add column if not exists client_id uuid references public.clients(id) on delete set null;
alter table public.tasks add column if not exists estimated_minutes integer not null default 0;
alter table public.tasks add column if not exists updated_at timestamptz default now();

create table if not exists public.task_collaborators (
  id uuid default uuid_generate_v4() primary key,
  task_id uuid references public.tasks(id) on delete cascade not null,
  user_id uuid references public.profiles(id) on delete cascade not null,
  role text not null default 'collaborator',
  created_at timestamptz default now(),
  unique(task_id,user_id)
);

create table if not exists public.task_comments (
  id uuid default uuid_generate_v4() primary key,
  task_id uuid references public.tasks(id) on delete cascade not null,
  user_id uuid references public.profiles(id) on delete cascade not null,
  content text not null,
  message_type varchar(20) not null default 'message',
  reply_to_id uuid references public.task_comments(id) on delete set null,
  updated_at timestamptz,
  deleted_at timestamptz,
  created_at timestamptz default now()
);
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
create index if not exists task_comment_attachments_comment_idx on public.task_comment_attachments(comment_id);
create index if not exists task_comment_attachments_task_idx on public.task_comment_attachments(task_id);

insert into storage.buckets(id,name,public,file_size_limit,allowed_mime_types)
values('task-chat','task-chat',false,8388608,array['image/jpeg','image/png','image/webp','image/gif'])
on conflict(id) do update set public=false,file_size_limit=excluded.file_size_limit,allowed_mime_types=excluded.allowed_mime_types;

create table if not exists public.task_checklists (
  id uuid default uuid_generate_v4() primary key,
  task_id uuid references public.tasks(id) on delete cascade not null,
  title text not null,
  is_completed boolean not null default false,
  position_index integer not null default 0,
  created_at timestamptz default now()
);

create table if not exists public.time_logs (
  id uuid default uuid_generate_v4() primary key,
  task_id uuid references public.tasks(id) on delete cascade not null,
  user_id uuid references public.profiles(id) on delete cascade not null,
  minutes integer not null check (minutes > 0),
  log_date date not null default current_date,
  description text,
  audit_hash text,
  created_at timestamptz default now()
);

create table if not exists public.activity_log (
  id uuid default uuid_generate_v4() primary key,
  task_id uuid references public.tasks(id) on delete cascade,
  board_id uuid references public.boards(id) on delete cascade,
  user_id uuid references public.profiles(id) on delete cascade not null,
  action text not null,
  details jsonb,
  audit_hash text,
  created_at timestamptz default now()
);

create table if not exists public.automations (
  id uuid default uuid_generate_v4() primary key,
  title varchar(255) not null,
  trigger_type varchar(50) not null,
  trigger_value varchar(50) not null,
  action_type varchar(50) not null,
  action_payload varchar(255),
  is_active boolean not null default true,
  created_at timestamptz default now()
);

create or replace function public.is_manager()
returns boolean language sql stable security definer set search_path = public
as $$ select exists(select 1 from public.profiles where id=auth.uid() and role in ('admin','manager')); $$;

create or replace function public.handle_new_user()
returns trigger language plpgsql security definer set search_path = public
as $$
begin
  insert into public.profiles(id,email,full_name,avatar_url)
  values(new.id,new.email,coalesce(new.raw_user_meta_data->>'full_name',split_part(new.email,'@',1)),new.raw_user_meta_data->>'avatar_url')
  on conflict(id) do nothing;
  insert into public.user_rates(user_id,hourly_rate) values(new.id,0) on conflict(user_id) do nothing;
  return new;
end $$;
drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created after insert on auth.users for each row execute function public.handle_new_user();

create or replace function public.touch_updated_at()
returns trigger language plpgsql as $$ begin new.updated_at=now(); return new; end $$;
drop trigger if exists update_tasks_updated_at on public.tasks;
create trigger update_tasks_updated_at before update on public.tasks for each row execute function public.touch_updated_at();

create or replace function public.recalculate_task_minutes()
returns trigger language plpgsql security definer set search_path = public
as $$
declare target uuid := case when tg_op='DELETE' then old.task_id else new.task_id end;
begin
  update public.tasks set total_minutes_spent=(select coalesce(sum(minutes),0) from public.time_logs where task_id=target)
  where id=target;
  return null;
end $$;
drop trigger if exists on_time_log_change on public.time_logs;
create trigger on_time_log_change after insert or update or delete on public.time_logs
for each row execute function public.recalculate_task_minutes();

create or replace function public.log_task_status_change()
returns trigger language plpgsql security definer set search_path = public
as $$
begin
  if old.status is distinct from new.status and auth.uid() is not null then
    insert into public.activity_log(task_id,board_id,user_id,action,details)
    values(new.id,new.board_id,auth.uid(),'status_changed',
      jsonb_build_object('old_status',old.status,'new_status',new.status,'task_title',new.title));
  end if;
  return new;
end $$;
drop trigger if exists on_task_status_change on public.tasks;
create trigger on_task_status_change after update of status on public.tasks
for each row execute function public.log_task_status_change();

create or replace function public.execute_task_automations()
returns trigger language plpgsql security definer set search_path = public
as $$
declare rule record;
begin
  for rule in select * from public.automations
    where is_active and trigger_type='status_change' and trigger_value=new.status
  loop
    if rule.action_type='assign_user' and rule.action_payload is not null then
      new.assigned_to=rule.action_payload::uuid;
    end if;
    if auth.uid() is not null then
      insert into public.activity_log(task_id,board_id,user_id,action,details)
      values(new.id,new.board_id,auth.uid(),'automation_fired',
        jsonb_build_object('automation',rule.title,'action',rule.action_type));
    end if;
  end loop;
  return new;
end $$;
drop trigger if exists execute_task_automations on public.tasks;
create trigger execute_task_automations before update of status on public.tasks
for each row when(old.status is distinct from new.status) execute function public.execute_task_automations();

create schema if not exists private;
revoke all on schema private from public, anon;
grant usage on schema private to authenticated;
create or replace function private.task_is_participant(target_task_id uuid)
returns boolean language sql stable security definer set search_path=''
as $$ select (select auth.uid()) is not null and
  exists(select 1 from public.task_collaborators c where c.task_id=target_task_id and c.user_id=(select auth.uid())) $$;
revoke execute on function private.task_is_participant(uuid) from public, anon, service_role;
grant execute on function private.task_is_participant(uuid) to authenticated;

alter table public.teams enable row level security;
alter table public.profiles enable row level security;
alter table public.user_rates enable row level security;
alter table public.clients enable row level security;
alter table public.boards enable row level security;
alter table public.tasks enable row level security;
alter table public.task_collaborators enable row level security;
alter table public.task_comments enable row level security;
alter table public.task_comment_attachments enable row level security;
alter table public.task_checklists enable row level security;
alter table public.time_logs enable row level security;
alter table public.activity_log enable row level security;
alter table public.automations enable row level security;

do $$
declare table_name text; policy_name text;
begin
  for table_name,policy_name in select * from (values
    ('teams','teams_read'),('teams','teams_manage'),('profiles','profiles_read'),('profiles','profiles_update_self'),
    ('user_rates','rates_manage'),('clients','clients_read'),('clients','clients_manage'),
    ('boards','boards_public_read'),('boards','boards_read'),('boards','boards_insert'),('boards','boards_update'),('boards','boards_delete'),
    ('tasks','tasks_read'),('tasks','tasks_insert'),('tasks','tasks_public_insert'),('tasks','tasks_update'),('tasks','tasks_delete'),
    ('task_collaborators','collaborators_manage'),('task_comments','comments_manage'),('task_checklists','checklists_manage'),
    ('time_logs','time_logs_read'),('time_logs','time_logs_insert'),('time_logs','time_logs_update'),('time_logs','time_logs_delete'),
    ('activity_log','activity_read'),('automations','automations_manage')
  ) as p(t,n)
  loop execute format('drop policy if exists %I on public.%I',policy_name,table_name); end loop;
end $$;

create policy teams_read on public.teams for select to authenticated using(true);
create policy teams_manage on public.teams for all to authenticated using(public.is_manager()) with check(public.is_manager());
create policy profiles_read on public.profiles for select to authenticated using(true);
create policy profiles_update_self on public.profiles for update to authenticated using(id=auth.uid() or public.is_manager()) with check(id=auth.uid() or public.is_manager());
create policy rates_manage on public.user_rates for all to authenticated using(public.is_manager()) with check(public.is_manager());
create policy clients_read on public.clients for select to authenticated using(true);
create policy clients_manage on public.clients for all to authenticated using(public.is_manager()) with check(public.is_manager());

create policy boards_public_read on public.boards for select to anon using(status='active');
create policy boards_read on public.boards for select to authenticated using(public.is_manager() or owner_id=auth.uid());
create policy boards_insert on public.boards for insert to authenticated with check(owner_id=auth.uid() or public.is_manager());
create policy boards_update on public.boards for update to authenticated using(owner_id=auth.uid() or public.is_manager());
create policy boards_delete on public.boards for delete to authenticated using(owner_id=auth.uid() or public.is_manager());

create policy tasks_read on public.tasks for select to authenticated using(
  public.is_manager() or assigned_to=auth.uid()
  or exists(select 1 from public.boards b where b.id=board_id and b.owner_id=auth.uid())
  or (select private.task_is_participant(public.tasks.id))
);
create policy tasks_insert on public.tasks for insert to authenticated with check(true);
create policy tasks_public_insert on public.tasks for insert to anon with check(status='backlog' and assigned_to is null);
create policy tasks_update on public.tasks for update to authenticated using(
  public.is_manager() or assigned_to=auth.uid() or exists(select 1 from public.boards b where b.id=board_id and b.owner_id=auth.uid())
);
create policy tasks_delete on public.tasks for delete to authenticated using(
  public.is_manager() or exists(select 1 from public.boards b where b.id=board_id and b.owner_id=auth.uid())
);

create policy collaborators_manage on public.task_collaborators for all to authenticated using(true) with check(true);
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
create policy checklists_manage on public.task_checklists for all to authenticated using(true) with check(true);
create policy time_logs_read on public.time_logs for select to authenticated using(user_id=auth.uid() or public.is_manager());
create policy time_logs_insert on public.time_logs for insert to authenticated with check(user_id=auth.uid());
create policy time_logs_update on public.time_logs for update to authenticated using(user_id=auth.uid() or public.is_manager());
create policy time_logs_delete on public.time_logs for delete to authenticated using(user_id=auth.uid() or public.is_manager());
create policy activity_read on public.activity_log for select to authenticated using(true);
create policy automations_manage on public.automations for all to authenticated using(public.is_manager()) with check(public.is_manager());

-- GestÃ£o de trabalho, handoffs, cronograma corporativo e faturamento
alter table public.tasks add column if not exists accountable_owner_id uuid references public.profiles(id) on delete set null;
alter table public.tasks add column if not exists created_by uuid references public.profiles(id) on delete set null;
alter table public.tasks add column if not exists workflow_state text not null default 'inbox'
  check(workflow_state in ('inbox','in_progress','waiting_external','waiting_review','changes_requested','done','cancelled'));
alter table public.tasks add column if not exists acceptance_by uuid references public.profiles(id) on delete set null;
alter table public.tasks add column if not exists accepted_at timestamptz;
update public.tasks t set accountable_owner_id=coalesce(t.accountable_owner_id,t.assigned_to,b.owner_id),
  created_by=coalesce(t.created_by,b.owner_id),
  workflow_state=case when t.status='done' then 'done' when t.assigned_to is null then 'waiting_external' else 'in_progress' end
from public.boards b where b.id=t.board_id and (t.accountable_owner_id is null or t.created_by is null);

alter table public.boards add column if not exists planned_start date;
alter table public.boards add column if not exists planned_end date;
alter table public.boards add column if not exists health text not null default 'on_track'
  check(health in ('on_track','at_risk','off_track','on_hold'));
alter table public.boards add column if not exists budget_amount numeric(14,2);

alter table public.time_logs add column if not exists is_billable boolean not null default true;
alter table public.time_logs add column if not exists approval_status text not null default 'pending'
  check(approval_status in ('pending','approved','rejected'));
alter table public.time_logs add column if not exists billing_status text not null default 'unbilled'
  check(billing_status in ('unbilled','invoiced','written_off'));
alter table public.time_logs add column if not exists cost_rate_snapshot numeric(12,2) not null default 0;
alter table public.time_logs add column if not exists billing_rate_snapshot numeric(12,2) not null default 0;
alter table public.time_logs add column if not exists approved_by uuid references public.profiles(id) on delete set null;
alter table public.time_logs add column if not exists approved_at timestamptz;
alter table public.time_logs add column if not exists invoice_id uuid;
update public.time_logs l set cost_rate_snapshot=r.hourly_rate
from public.user_rates r where r.user_id=l.user_id and l.cost_rate_snapshot=0;

create table if not exists public.notifications (
  id uuid default uuid_generate_v4() primary key,
  recipient_id uuid references public.profiles(id) on delete cascade not null,
  actor_id uuid references public.profiles(id) on delete set null,
  task_id uuid references public.tasks(id) on delete cascade,
  board_id uuid references public.boards(id) on delete cascade,
  type varchar(60) not null,
  title varchar(255) not null,
  message text,
  action_url text,
  priority varchar(20) not null default 'normal' check(priority in ('low','normal','high','critical')),
  deduplication_key text,
  read_at timestamptz,
  archived_at timestamptz,
  created_at timestamptz not null default now()
);
-- Compatibilidade com instalaÃ§Ãµes que jÃ¡ possuÃ­am uma tabela notifications antiga.
-- CREATE TABLE IF NOT EXISTS nÃ£o adiciona colunas ausentes em tabelas existentes.
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
create unique index if not exists notifications_deduplication_key_idx on public.notifications(deduplication_key);
create index if not exists notifications_recipient_unread_idx on public.notifications(recipient_id,created_at desc) where read_at is null;

create table if not exists public.task_assignments (
  id uuid default uuid_generate_v4() primary key,
  task_id uuid references public.tasks(id) on delete cascade not null,
  from_user_id uuid references public.profiles(id) on delete set null,
  to_user_id uuid references public.profiles(id) on delete cascade not null,
  assigned_by uuid references public.profiles(id) on delete restrict not null,
  stage varchar(100) not null,
  status varchar(30) not null default 'pending' check(status in ('pending','accepted','rejected','completed','changes_requested','cancelled')),
  notes text,
  acceptance_criteria text,
  response_note text,
  due_date timestamptz,
  estimated_minutes integer not null default 0 check(estimated_minutes >= 0),
  requires_acceptance boolean not null default false,
  acceptance_by uuid references public.profiles(id) on delete set null,
  accepted_at timestamptz,
  completed_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists task_assignments_task_idx on public.task_assignments(task_id,created_at desc);
create index if not exists task_assignments_inbox_idx on public.task_assignments(to_user_id,status,created_at desc);

create table if not exists public.task_followers (
  id uuid default uuid_generate_v4() primary key,
  task_id uuid references public.tasks(id) on delete cascade not null,
  user_id uuid references public.profiles(id) on delete cascade not null,
  reason varchar(50) not null default 'manual',
  created_at timestamptz not null default now(),
  unique(task_id,user_id)
);

create table if not exists public.task_dependencies (
  id uuid default uuid_generate_v4() primary key,
  task_id uuid references public.tasks(id) on delete cascade not null,
  depends_on_task_id uuid references public.tasks(id) on delete cascade not null,
  dependency_type varchar(30) not null default 'finish_to_start',
  created_at timestamptz not null default now(),
  check(task_id <> depends_on_task_id),
  unique(task_id,depends_on_task_id)
);
create index if not exists task_dependencies_prerequisite_idx on public.task_dependencies(depends_on_task_id);

create or replace function public.prevent_task_dependency_cycle()
returns trigger language plpgsql security invoker set search_path=public as $$
begin
  if exists (
    with recursive dependency_chain(task_id) as (
      select new.depends_on_task_id
      union
      select dependency.depends_on_task_id
      from public.task_dependencies dependency
      join dependency_chain chain on dependency.task_id=chain.task_id
      where tg_op='INSERT' or dependency.id<>new.id
    )
    select 1 from dependency_chain where task_id=new.task_id
  ) then
    raise exception 'Esta dependência criaria um ciclo entre as tarefas';
  end if;
  return new;
end $$;
drop trigger if exists prevent_task_dependency_cycle on public.task_dependencies;
create trigger prevent_task_dependency_cycle
before insert or update of task_id,depends_on_task_id on public.task_dependencies
for each row execute function public.prevent_task_dependency_cycle();

create table if not exists public.project_milestones (
  id uuid default uuid_generate_v4() primary key,
  board_id uuid references public.boards(id) on delete cascade not null,
  title varchar(255) not null,
  due_date date not null,
  status varchar(30) not null default 'planned' check(status in ('planned','achieved','missed','cancelled')),
  created_at timestamptz not null default now()
);

create table if not exists public.work_schedules (
  id uuid default uuid_generate_v4() primary key,
  user_id uuid references public.profiles(id) on delete cascade,
  team_id uuid references public.teams(id) on delete cascade,
  weekly_capacity_minutes integer not null default 2400 check(weekly_capacity_minutes >= 0),
  work_days varchar(20) not null default '1,2,3,4,5',
  valid_from date not null default current_date,
  valid_to date,
  created_at timestamptz not null default now(),
  check(user_id is not null or team_id is not null)
);

create table if not exists public.client_contracts (
  id uuid default uuid_generate_v4() primary key,
  client_id uuid references public.clients(id) on delete cascade not null,
  board_id uuid references public.boards(id) on delete set null,
  name varchar(255) not null,
  contract_type varchar(30) not null check(contract_type in ('hourly','fixed','retainer','hour_bank','internal')),
  billing_rate numeric(12,2) not null default 0 check(billing_rate >= 0),
  budget_amount numeric(14,2),
  included_minutes integer,
  starts_on date not null default current_date,
  ends_on date,
  is_active boolean not null default true,
  created_at timestamptz not null default now()
);

create or replace function public.snapshot_time_log_rates()
returns trigger language plpgsql security definer set search_path=public as $$
declare target public.tasks%rowtype; contract_rate numeric(12,2);
begin
  select * into target from public.tasks where id=new.task_id;
  if new.cost_rate_snapshot=0 then
    select coalesce(hourly_rate,0) into new.cost_rate_snapshot from public.user_rates where user_id=new.user_id;
    new.cost_rate_snapshot:=coalesce(new.cost_rate_snapshot,0);
  end if;
  if new.is_billable and new.billing_rate_snapshot=0 then
    select billing_rate into contract_rate from public.client_contracts
      where is_active and client_id=target.client_id and (board_id is null or board_id=target.board_id)
      and starts_on<=new.log_date and (ends_on is null or ends_on>=new.log_date)
      order by (board_id is not null) desc,starts_on desc limit 1;
    new.billing_rate_snapshot:=coalesce(contract_rate,0);
  end if;
  if not new.is_billable then new.billing_rate_snapshot:=0; end if;
  return new;
end $$;
drop trigger if exists snapshot_time_log_rates on public.time_logs;
create trigger snapshot_time_log_rates before insert on public.time_logs for each row execute function public.snapshot_time_log_rates();

create table if not exists public.billing_invoices (
  id uuid default uuid_generate_v4() primary key,
  client_id uuid references public.clients(id) on delete restrict not null,
  contract_id uuid references public.client_contracts(id) on delete set null,
  reference varchar(80) not null unique,
  status varchar(30) not null default 'draft' check(status in ('draft','issued','paid','cancelled')),
  period_start date not null,
  period_end date not null,
  due_date date,
  subtotal numeric(14,2) not null default 0,
  total numeric(14,2) not null default 0,
  created_by uuid references public.profiles(id) on delete restrict not null,
  created_at timestamptz not null default now()
);

create table if not exists public.billing_invoice_items (
  id uuid default uuid_generate_v4() primary key,
  invoice_id uuid references public.billing_invoices(id) on delete cascade not null,
  time_log_id uuid references public.time_logs(id) on delete set null,
  description text not null,
  minutes integer not null default 0,
  unit_rate numeric(12,2) not null default 0,
  amount numeric(14,2) not null default 0,
  created_at timestamptz not null default now()
);
alter table public.time_logs drop constraint if exists time_logs_invoice_id_fkey;
alter table public.time_logs add constraint time_logs_invoice_id_fkey foreign key(invoice_id) references public.billing_invoices(id) on delete set null;

create or replace function public.is_admin()
returns boolean language sql stable security definer set search_path=public
as $$ select exists(select 1 from public.profiles where id=auth.uid() and role='admin') $$;

create or replace function public.current_team_id()
returns uuid language sql stable security definer set search_path=public
as $$ select team_id from public.profiles where id=auth.uid() $$;

create or replace function public.can_manage_user(target_user uuid)
returns boolean language sql stable security definer set search_path=public
as $$
  select public.is_admin() or exists(
    select 1 from public.profiles me join public.profiles target on target.id=target_user
    where me.id=auth.uid() and me.role='manager' and me.team_id is not null and me.team_id=target.team_id
  )
$$;

create or replace function public.notify_task_assignment_change()
returns trigger language plpgsql security definer set search_path=public as $$
declare actor uuid := auth.uid();
begin
  if tg_op='INSERT' and new.assigned_to is not null and actor is not null then
    insert into public.task_assignments(task_id,to_user_id,assigned_by,stage,status,accepted_at,estimated_minutes,due_date)
    values(new.id,new.assigned_to,actor,new.status,
      case when actor=new.assigned_to then 'accepted' else 'pending' end,
      case when actor=new.assigned_to then now() else null end,new.estimated_minutes,new.due_date);
  end if;
  if tg_op='UPDATE' and new.assigned_to is not null and old.assigned_to is distinct from new.assigned_to and actor is not null
    and not exists(select 1 from public.task_assignments where task_id=new.id and to_user_id=new.assigned_to and status in ('pending','accepted')) then
    update public.task_assignments set status='completed',completed_at=now(),updated_at=now()
      where task_id=new.id and status in ('pending','accepted');
    insert into public.task_assignments(task_id,from_user_id,to_user_id,assigned_by,stage,status,estimated_minutes,due_date)
      values(new.id,old.assigned_to,new.assigned_to,actor,new.status,
        case when actor=new.assigned_to then 'accepted' else 'pending' end,new.estimated_minutes,new.due_date);
    update public.tasks set workflow_state=case when actor=new.assigned_to then 'in_progress' else 'inbox' end where id=new.id;
  end if;
  if new.assigned_to is not null and (actor is null or actor<>new.assigned_to)
    and (tg_op='INSERT' or old.assigned_to is distinct from new.assigned_to) then
    if tg_op='UPDATE' and old.assigned_to is not null then
      insert into public.task_followers(task_id,user_id,reason)
      values(new.id,old.assigned_to,'previous_assignee') on conflict(task_id,user_id) do nothing;
    end if;
    insert into public.notifications(recipient_id,actor_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
    values(new.assigned_to,actor,new.id,new.board_id,'assignment_received','Nova tarefa atribuÃ­da',new.title,
      '/Boards/Details/'||new.board_id::text,'high','assignment:'||new.id::text||':'||new.assigned_to::text||':'||new.updated_at::text)
    on conflict(deduplication_key) do nothing;
  end if;
  return new;
end $$;
drop trigger if exists task_assignment_notification on public.tasks;
create trigger task_assignment_notification after insert or update of assigned_to on public.tasks
for each row execute function public.notify_task_assignment_change();

create or replace function public.notify_task_comment()
returns trigger language plpgsql security definer set search_path=public as $$
declare target public.tasks%rowtype; recipient uuid;
begin
  select * into target from public.tasks where id=new.task_id;
  for recipient in
    select distinct user_id from (
      select target.assigned_to as user_id union all select target.accountable_owner_id union all
      select user_id from public.task_followers where task_id=new.task_id union all
      select user_id from public.task_collaborators where task_id=new.task_id
    ) involved where user_id is not null and user_id<>new.user_id
  loop
    insert into public.notifications(recipient_id,actor_id,task_id,board_id,type,title,message,action_url,deduplication_key)
    values(recipient,new.user_id,new.task_id,target.board_id,'comment_added','Novo comentÃ¡rio',left(new.content,300),
      '/Boards/Details/'||target.board_id::text,'comment:'||new.id::text||':'||recipient::text)
    on conflict(deduplication_key) do nothing;
  end loop;
  return new;
end $$;
drop trigger if exists task_comment_notification on public.task_comments;
create trigger task_comment_notification after insert on public.task_comments for each row execute function public.notify_task_comment();

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

create or replace function public.ensure_due_notifications()
returns integer language plpgsql security definer set search_path=public as $$
declare item public.tasks%rowtype; kind text; inserted_count integer := 0;
begin
  for item in select * from public.tasks where status<>'done' and due_date is not null
    and (assigned_to=auth.uid() or accountable_owner_id=auth.uid()) and due_date < now()+interval '1 day'
  loop
    kind := case when item.due_date<now() then 'task_overdue' else 'task_due_soon' end;
    insert into public.notifications(recipient_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
    values(auth.uid(),item.id,item.board_id,kind,
      case when kind='task_overdue' then 'Tarefa atrasada' else 'Prazo prÃ³ximo' end,item.title,
      '/Boards/Details/'||item.board_id::text,case when kind='task_overdue' then 'critical' else 'high' end,
      kind||':'||item.id::text||':'||current_date::text)
    on conflict(deduplication_key) do nothing;
    if found then inserted_count := inserted_count+1; end if;
  end loop;
  return inserted_count;
end $$;

create or replace function public.handoff_task(
  p_task_id uuid,
  p_to_user_id uuid,
  p_stage text,
  p_due_date timestamptz default null,
  p_estimated_minutes integer default 0,
  p_notes text default null,
  p_acceptance_criteria text default null,
  p_requires_acceptance boolean default false,
  p_acceptance_by uuid default null)
returns uuid language plpgsql security invoker set search_path=public as $$
declare current_task public.tasks%rowtype; assignment_id uuid;
begin
  select * into current_task from public.tasks where id=p_task_id for update;
  if current_task.id is null then raise exception 'Tarefa nÃ£o encontrada'; end if;
  if p_to_user_id is null then raise exception 'Novo executor obrigatÃ³rio'; end if;

  update public.task_assignments set status='completed',completed_at=now(),updated_at=now()
  where task_id=p_task_id and status in ('pending','accepted');
  insert into public.task_assignments(task_id,from_user_id,to_user_id,assigned_by,stage,notes,acceptance_criteria,due_date,
    estimated_minutes,requires_acceptance,acceptance_by)
  values(p_task_id,current_task.assigned_to,p_to_user_id,auth.uid(),trim(p_stage),nullif(trim(p_notes),''),
    nullif(trim(p_acceptance_criteria),''),p_due_date,greatest(0,p_estimated_minutes),p_requires_acceptance,
    case when p_requires_acceptance then coalesce(p_acceptance_by,current_task.accountable_owner_id) else null end)
  returning id into assignment_id;

  if current_task.assigned_to is not null then
    insert into public.task_followers(task_id,user_id,reason) values(p_task_id,current_task.assigned_to,'handoff')
    on conflict(task_id,user_id) do nothing;
  end if;
  if current_task.accountable_owner_id is not null then
    insert into public.task_followers(task_id,user_id,reason) values(p_task_id,current_task.accountable_owner_id,'accountable')
    on conflict(task_id,user_id) do nothing;
  end if;

  update public.tasks set assigned_to=p_to_user_id,status=trim(p_stage),workflow_state='inbox',due_date=coalesce(p_due_date,due_date),
    estimated_minutes=case when p_estimated_minutes>0 then p_estimated_minutes else estimated_minutes end,
    acceptance_by=case when p_requires_acceptance then coalesce(p_acceptance_by,current_task.accountable_owner_id) else null end
  where id=p_task_id;
  insert into public.activity_log(task_id,board_id,user_id,action,details)
  values(p_task_id,current_task.board_id,auth.uid(),'task_handed_off',jsonb_build_object('from',current_task.assigned_to,'to',p_to_user_id,'stage',p_stage));
  return assignment_id;
end $$;

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
  if p_to_user_id=current_task.assigned_to then
    raise exception 'Selecione outra pessoa para responder';
  end if;

  insert into public.task_comments(task_id,user_id,content,message_type,created_at)
  values(p_task_id,(select auth.uid()),trim(p_question),'question',now()) returning id into comment_id;

  perform public.handoff_task(
    p_task_id,p_to_user_id,current_task.status,current_task.due_date,current_task.estimated_minutes,
    p_question,null,false,null
  );
  return comment_id;
end $$;

create or replace function public.respond_task_assignment(p_assignment_id uuid,p_action text,p_note text default null)
returns boolean language plpgsql security invoker set search_path=public as $$
declare assignment public.task_assignments%rowtype; current_task public.tasks%rowtype;
begin
  select * into assignment from public.task_assignments where id=p_assignment_id for update;
  if assignment.id is null then raise exception 'AtribuiÃ§Ã£o nÃ£o encontrada'; end if;
  if assignment.to_user_id<>auth.uid() and not public.can_manage_user(assignment.to_user_id) then raise exception 'Sem permissÃ£o'; end if;
  select * into current_task from public.tasks where id=assignment.task_id for update;

  if p_action='accept' then
    if exists(select 1 from public.task_dependencies d join public.tasks prerequisite on prerequisite.id=d.depends_on_task_id
      where d.task_id=assignment.task_id and prerequisite.status<>'done') then
      raise exception 'Existem dependÃªncias ainda nÃ£o concluÃ­das';
    end if;
    update public.task_assignments set status='accepted',accepted_at=now(),response_note=nullif(trim(p_note),''),updated_at=now() where id=p_assignment_id;
    update public.tasks set workflow_state='in_progress' where id=assignment.task_id;
  elsif p_action='reject' then
    update public.task_assignments set status='rejected',response_note=nullif(trim(p_note),''),updated_at=now() where id=p_assignment_id;
    insert into public.task_comments(task_id,user_id,content,message_type,created_at)
    values(assignment.task_id,(select auth.uid()),trim(p_note),'question',now());
    if assignment.from_user_id is not null then
      insert into public.task_assignments(task_id,from_user_id,to_user_id,assigned_by,stage,status,notes,due_date,estimated_minutes)
      values(assignment.task_id,assignment.to_user_id,assignment.from_user_id,auth.uid(),current_task.status,'pending',
        nullif(trim(p_note),''),current_task.due_date,current_task.estimated_minutes);
    end if;
    update public.tasks set assigned_to=assignment.from_user_id,workflow_state='changes_requested' where id=assignment.task_id;
  elsif p_action='complete' then
    update public.task_assignments set status='completed',completed_at=now(),response_note=nullif(trim(p_note),''),updated_at=now() where id=p_assignment_id;
    if assignment.requires_acceptance and assignment.acceptance_by is not null then
      update public.tasks set assigned_to=assignment.acceptance_by,workflow_state='waiting_review' where id=assignment.task_id;
    else
      update public.tasks set workflow_state='done',status='done',completed_at=now() where id=assignment.task_id;
    end if;
  else raise exception 'AÃ§Ã£o invÃ¡lida';
  end if;
  insert into public.activity_log(task_id,board_id,user_id,action,details)
  values(assignment.task_id,current_task.board_id,auth.uid(),'assignment_'||p_action,
    jsonb_build_object('assignment_id',assignment.id,'note',p_note));
  if assignment.assigned_by<>auth.uid() then
    insert into public.notifications(recipient_id,actor_id,task_id,board_id,type,title,message,action_url,deduplication_key)
    values(assignment.assigned_by,auth.uid(),assignment.task_id,current_task.board_id,'assignment_response',
      case p_action when 'accept' then 'AtribuiÃ§Ã£o aceita' when 'reject' then 'AtribuiÃ§Ã£o recusada' else 'Etapa concluÃ­da' end,
      current_task.title,'/Boards/Details/'||current_task.board_id::text,
      'assignment-response:'||assignment.id::text||':'||p_action)
    on conflict(deduplication_key) do nothing;
  end if;
  return true;
end $$;

create or replace function public.review_task(p_task_id uuid,p_action text,p_note text default null)
returns boolean language plpgsql security invoker set search_path=public as $$
declare current_task public.tasks%rowtype; previous_executor uuid;
begin
  select * into current_task from public.tasks where id=p_task_id for update;
  if current_task.id is null or current_task.workflow_state<>'waiting_review' then raise exception 'Tarefa nÃ£o aguarda revisÃ£o'; end if;
  if current_task.acceptance_by<>auth.uid() and not public.can_manage_user(current_task.acceptance_by) then raise exception 'Sem permissÃ£o'; end if;
  if p_action='approve' then
    update public.tasks set workflow_state='done',status='done',completed_at=now(),accepted_at=now() where id=p_task_id;
  elsif p_action='changes' then
    select to_user_id into previous_executor from public.task_assignments where task_id=p_task_id and status='completed' order by completed_at desc limit 1;
    if previous_executor is null then raise exception 'Executor anterior nÃ£o encontrado'; end if;
    insert into public.task_assignments(task_id,from_user_id,to_user_id,assigned_by,stage,status,notes,due_date,estimated_minutes)
    values(p_task_id,auth.uid(),previous_executor,auth.uid(),current_task.status,'pending',nullif(trim(p_note),''),
      current_task.due_date,current_task.estimated_minutes);
    insert into public.task_comments(task_id,user_id,content,message_type,created_at)
    values(p_task_id,(select auth.uid()),trim(p_note),'question',now());
    update public.tasks set assigned_to=previous_executor,workflow_state='changes_requested',completed_at=null where id=p_task_id;
  else raise exception 'AÃ§Ã£o invÃ¡lida'; end if;
  insert into public.activity_log(task_id,board_id,user_id,action,details)
  values(p_task_id,current_task.board_id,auth.uid(),'task_reviewed',jsonb_build_object('result',p_action,'note',p_note));
  return true;
end $$;

alter table public.notifications enable row level security;
alter table public.task_assignments enable row level security;
alter table public.task_followers enable row level security;
create or replace function private.task_is_participant(target_task_id uuid)
returns boolean language sql stable security definer set search_path=''
as $$ select (select auth.uid()) is not null and (
  exists(select 1 from public.task_collaborators c where c.task_id=target_task_id and c.user_id=(select auth.uid()))
  or exists(select 1 from public.task_followers f where f.task_id=target_task_id and f.user_id=(select auth.uid()))
) $$;
alter table public.task_dependencies enable row level security;
alter table public.project_milestones enable row level security;
alter table public.work_schedules enable row level security;
alter table public.client_contracts enable row level security;
alter table public.billing_invoices enable row level security;
alter table public.billing_invoice_items enable row level security;

drop policy if exists boards_read on public.boards;
create policy boards_read on public.boards for select to authenticated using(
  public.is_admin() or owner_id=auth.uid() or exists(
    select 1 from public.profiles me join public.profiles owner on owner.id=owner_id
    where me.id=auth.uid() and me.role='manager' and me.team_id is not null and me.team_id=owner.team_id));
drop policy if exists tasks_read on public.tasks;
create policy tasks_read on public.tasks for select to authenticated using(
  public.is_admin() or assigned_to=auth.uid() or accountable_owner_id=auth.uid() or created_by=auth.uid()
  or exists(select 1 from public.boards b where b.id=board_id and b.owner_id=auth.uid())
  or (select private.task_is_participant(public.tasks.id))
  or (assigned_to is not null and public.can_manage_user(assigned_to)));
drop policy if exists tasks_update on public.tasks;
create policy tasks_update on public.tasks for update to authenticated using(
  public.is_admin() or assigned_to=auth.uid() or accountable_owner_id=auth.uid()
  or exists(select 1 from public.boards b where b.id=board_id and b.owner_id=auth.uid())
  or (assigned_to is not null and public.can_manage_user(assigned_to)));

drop policy if exists notifications_own on public.notifications;
create policy notifications_own on public.notifications for select to authenticated using(recipient_id=auth.uid());
drop policy if exists notifications_update_own on public.notifications;
create policy notifications_update_own on public.notifications for update to authenticated using(recipient_id=auth.uid()) with check(recipient_id=auth.uid());
drop policy if exists assignments_read on public.task_assignments;
create policy assignments_read on public.task_assignments for select to authenticated using(
  to_user_id=auth.uid() or from_user_id=auth.uid() or assigned_by=auth.uid() or public.can_manage_user(to_user_id));
drop policy if exists assignments_insert on public.task_assignments;
create policy assignments_insert on public.task_assignments for insert to authenticated with check(assigned_by=auth.uid());
drop policy if exists assignments_update on public.task_assignments;
create policy assignments_update on public.task_assignments for update to authenticated using(
  to_user_id=auth.uid() or assigned_by=auth.uid() or public.can_manage_user(to_user_id)
  or exists(select 1 from public.tasks t where t.id=task_id and t.accountable_owner_id=auth.uid()));
drop policy if exists followers_read on public.task_followers;
create policy followers_read on public.task_followers for select to authenticated using(user_id=auth.uid() or public.is_manager());
drop policy if exists followers_manage on public.task_followers;
create policy followers_manage on public.task_followers for all to authenticated using(user_id=auth.uid() or public.is_manager()) with check(true);
drop policy if exists dependencies_manage on public.task_dependencies;
drop policy if exists dependencies_select on public.task_dependencies;
create policy dependencies_select on public.task_dependencies for select to authenticated using(
  exists(select 1 from public.tasks visible_task where visible_task.id=task_dependencies.task_id)
);
drop policy if exists dependencies_insert on public.task_dependencies;
create policy dependencies_insert on public.task_dependencies for insert to authenticated with check(
  exists(select 1 from public.tasks visible_task where visible_task.id=task_dependencies.task_id)
  and exists(select 1 from public.tasks prerequisite where prerequisite.id=task_dependencies.depends_on_task_id)
);
drop policy if exists dependencies_delete on public.task_dependencies;
create policy dependencies_delete on public.task_dependencies for delete to authenticated using(
  exists(select 1 from public.tasks visible_task where visible_task.id=task_dependencies.task_id)
);
drop policy if exists milestones_read on public.project_milestones;
create policy milestones_read on public.project_milestones for select to authenticated using(true);
drop policy if exists milestones_manage on public.project_milestones;
create policy milestones_manage on public.project_milestones for all to authenticated using(public.is_manager()) with check(public.is_manager());
drop policy if exists schedules_read on public.work_schedules;
create policy schedules_read on public.work_schedules for select to authenticated using(user_id=auth.uid() or public.is_manager());
drop policy if exists schedules_manage on public.work_schedules;
create policy schedules_manage on public.work_schedules for all to authenticated using(public.is_manager()) with check(public.is_manager());
drop policy if exists contracts_read on public.client_contracts;
create policy contracts_read on public.client_contracts for select to authenticated using(public.is_manager());
drop policy if exists contracts_manage on public.client_contracts;
create policy contracts_manage on public.client_contracts for all to authenticated using(public.is_manager()) with check(public.is_manager());
drop policy if exists invoices_read on public.billing_invoices;
create policy invoices_read on public.billing_invoices for select to authenticated using(public.is_manager());
drop policy if exists invoices_manage on public.billing_invoices;
create policy invoices_manage on public.billing_invoices for all to authenticated using(public.is_manager()) with check(public.is_manager());
drop policy if exists invoice_items_read on public.billing_invoice_items;
create policy invoice_items_read on public.billing_invoice_items for select to authenticated using(public.is_manager());
drop policy if exists invoice_items_manage on public.billing_invoice_items;
create policy invoice_items_manage on public.billing_invoice_items for all to authenticated using(public.is_manager()) with check(public.is_manager());
drop policy if exists activity_insert on public.activity_log;
create policy activity_insert on public.activity_log for insert to authenticated with check(user_id=auth.uid());
drop policy if exists rates_read_self on public.user_rates;
create policy rates_read_self on public.user_rates for select to authenticated using(user_id=auth.uid() or public.is_admin() or public.can_manage_user(user_id));

drop policy if exists boards_update on public.boards;
create policy boards_update on public.boards for update to authenticated using(
  public.is_admin() or owner_id=auth.uid() or exists(
    select 1 from public.profiles me join public.profiles owner on owner.id=owner_id
    where me.id=auth.uid() and me.role='manager' and me.team_id is not null and me.team_id=owner.team_id));
drop policy if exists boards_delete on public.boards;
create policy boards_delete on public.boards for delete to authenticated using(public.is_admin() or owner_id=auth.uid());
drop policy if exists time_logs_read on public.time_logs;
create policy time_logs_read on public.time_logs for select to authenticated using(
  user_id=auth.uid() or public.is_admin() or public.can_manage_user(user_id));
drop policy if exists time_logs_update on public.time_logs;
create policy time_logs_update on public.time_logs for update to authenticated using(
  user_id=auth.uid() or public.is_admin() or public.can_manage_user(user_id));
drop policy if exists time_logs_delete on public.time_logs;
create policy time_logs_delete on public.time_logs for delete to authenticated using(
  user_id=auth.uid() or public.is_admin() or public.can_manage_user(user_id));

revoke execute on function public.handoff_task(uuid,uuid,text,timestamptz,integer,text,text,boolean,uuid) from public,anon;
revoke execute on function public.return_task_with_question(uuid,uuid,text) from public,anon;
revoke execute on function public.respond_task_assignment(uuid,text,text) from public,anon;
revoke execute on function public.review_task(uuid,text,text) from public,anon;
revoke execute on function public.ensure_due_notifications() from public,anon;
grant execute on function public.handoff_task(uuid,uuid,text,timestamptz,integer,text,text,boolean,uuid) to authenticated;
grant execute on function public.return_task_with_question(uuid,uuid,text) to authenticated;
grant execute on function public.respond_task_assignment(uuid,text,text) to authenticated;
grant execute on function public.review_task(uuid,text,text) to authenticated;
grant execute on function public.ensure_due_notifications() to authenticated;
grant select,insert,delete on public.task_dependencies to authenticated;
