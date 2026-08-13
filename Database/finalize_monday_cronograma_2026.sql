-- Completa o histórico de atribuição sem gerar centenas de alertas legados.
alter table public.task_assignments disable trigger user;
insert into public.task_assignments(id,task_id,from_user_id,to_user_id,assigned_by,stage,status,notes,due_date,estimated_minutes,requires_acceptance,completed_at,created_at,updated_at)
select md5('monday-assignment:'||(t.custom_fields->>'monday_item_id'))::uuid,t.id,null,t.assigned_to,
 'e4481907-3fdb-430d-a021-98f22d644152'::uuid,coalesce(t.custom_fields->>'monday_original_status',t.status),
 case when t.status='done' then 'completed' else 'accepted' end,
 'Atribuição importada do Monday sem reenvio de alerta histórico.',t.due_date,t.estimated_minutes,false,
 t.completed_at,coalesce(t.start_date,t.created_at),coalesce(t.completed_at,t.updated_at)
from public.tasks t
where t.board_id='a9d4b931-a730-3fec-8b8d-7517c1b12710' and t.assigned_to is not null
on conflict(id) do nothing;
alter table public.task_assignments enable trigger user;

insert into public.notifications(recipient_id,user_id,actor_id,board_id,type,title,message,action_url,priority,deduplication_key)
select t.assigned_to,t.assigned_to,'e4481907-3fdb-430d-a021-98f22d644152'::uuid,t.board_id,
 'monday_import','Atividades do Monday importadas',
 count(*)::text||' atividade(s) do cronograma de 2026 foram atribuídas a você.',
 '/Boards/Details/a9d4b931-a730-3fec-8b8d-7517c1b12710','normal','monday-import-2026:'||t.assigned_to
from public.tasks t
where t.board_id='a9d4b931-a730-3fec-8b8d-7517c1b12710' and t.assigned_to is not null
 and t.status<>'done' and t.archived_at is null
group by t.assigned_to,t.board_id
on conflict(deduplication_key) where deduplication_key is not null do nothing;
