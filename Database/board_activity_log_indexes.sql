-- Índices que sustentam as políticas RLS e o feed de atividades por Board/tarefa.
create index if not exists activity_log_board_created_idx
  on public.activity_log (board_id, created_at desc);

create index if not exists activity_log_task_created_idx
  on public.activity_log (task_id, created_at desc)
  where task_id is not null;

create index if not exists activity_log_user_idx
  on public.activity_log (user_id)
  where user_id is not null;

