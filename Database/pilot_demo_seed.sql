-- Cenário fictício, aditivo e idempotente. Não altera perfis, taxas ou projetos existentes.
begin;
do $$
#variable_conflict use_variable
declare
 owner_id uuid; people uuid[]; boards uuid[]:='{}'; clients uuid[]:='{}'; tasks uuid[]:='{}';
 board_id uuid; client_id uuid; task_id uuid; assignment_id uuid; log_id uuid; invoice_id uuid;
 executor_id uuid; idx integer; project_index integer; titles text[]:=array[
 'Mapear processos de pedidos','Validar integração com ERP','Desenvolver painel de entregas',
 'Homologar rastreamento','Preparar plano de treinamento','Publicar versão de homologação',
 'Restabelecer acesso financeiro','Ajustar exportação de notas fiscais','Investigar lentidão no fechamento',
 'Revisar permissões de estoque','Atualizar certificado de integração','Validar backup do portal'];
begin
 if exists(select 1 from public.boards where name='DEMO — Horizonte Logística | Implantação') then
  raise notice 'Cenário já instalado; nenhum registro foi alterado.'; return;
 end if;
 select id into owner_id from public.profiles where role='admin' and is_active order by id limit 1;
 select array_agg(id order by id) into people from(select id from public.profiles where role='user' and is_active order by id limit 3)p;
 if owner_id is null or coalesce(array_length(people,1),0)<2 then raise exception 'É necessário um administrador e ao menos dois executores ativos.'; end if;
 perform set_config('request.jwt.claims',jsonb_build_object('sub',owner_id,'role','authenticated')::text,true);
 for project_index in 1..2 loop
  insert into public.clients(name,email) values(case project_index when 1 then 'DEMO — Horizonte Logística' else 'DEMO — Clínica Aurora' end,
  case project_index when 1 then 'financeiro@horizonte.example.invalid' else 'operacoes@aurora.example.invalid' end) returning id into client_id;
  clients:=array_append(clients,client_id);
  insert into public.boards(name,description,owner_id,operation_profile,planned_start,planned_end,budget_amount,revenue_budget,settings)
  values(case project_index when 1 then 'DEMO — Horizonte Logística | Implantação' else 'DEMO — Clínica Aurora | Atendimento' end,
  'Cenário fictício do piloto empresarial. Não representa compromissos, valores ou clientes reais.',owner_id,
  case project_index when 1 then 'delivery' else 'service' end,current_date-7,current_date+21,
  case project_index when 1 then 12000 else 6000 end,case project_index when 1 then 24000 else 10000 end,
  '[{"id":"todo","title":"A fazer","color":"#64748b"},{"id":"in-progress","title":"Em execução","color":"#6366f1"},{"id":"review","title":"Homologação","color":"#f59e0b","requires_approval":true},{"id":"done","title":"Concluído","color":"#10b981"}]') returning id into board_id;
  boards:=array_append(boards,board_id);
  insert into public.client_contracts(client_id,board_id,name,contract_type,billing_rate,budget_amount,starts_on,ends_on)
  values(client_id,board_id,'DEMO — Contrato de homologação por hora','hourly',case project_index when 1 then 150 else 180 end,
  case project_index when 1 then 24000 else 10000 end,date_trunc('month',current_date)::date,current_date+90);
  insert into public.project_milestones(board_id,title,due_date) values(board_id,'DEMO — Validação do piloto',current_date+14);
 end loop;
 for idx in 1..12 loop
  project_index:=case when idx<=6 then 1 else 2 end;
  executor_id:=people[1+(idx-1)%array_length(people,1)];
  insert into public.tasks(board_id,title,description,assigned_to,accountable_owner_id,created_by,client_id,
  priority,start_date,due_date,target_month,estimated_minutes,is_blocked,blocker_reason,sla_minutes)
  values(boards[project_index],titles[idx],'DEMO — Atividade fictícia para validar planejamento, colaboração e faturamento.',
  executor_id,owner_id,owner_id,clients[project_index],case when idx in(3,7,9) then 'high' else 'medium' end,
  current_date-7,current_date+case when idx in(1,3,7,10) then -1 when idx in(4,8,9) then 1 else 7 end,
  to_char(current_date,'YYYY-MM'),case when idx<=6 then 360 else 120 end,idx=9,case when idx=9 then 'DEMO — Aguardando evidências do cliente.' end,
  case when project_index=2 then 480 end) returning id into task_id;
  tasks:=array_append(tasks,task_id);
  insert into public.task_comments(task_id,user_id,content) values(task_id,owner_id,'DEMO — Critérios de aceite: evidência registrada e validação do solicitante.');
  insert into public.task_checklists(task_id,title,is_completed,position_index) values(task_id,'Registrar evidência da execução',idx in(1,10),0),(task_id,'Validar resultado com o solicitante',idx in(1,10),1);
  if idx in(1,2,3,4,8,10) then
   perform set_config('request.jwt.claims',jsonb_build_object('sub',executor_id,'role','authenticated')::text,true);
   select id into assignment_id from public.task_assignments where task_assignments.task_id=task_id and status='pending' order by created_at desc limit 1;
   perform public.respond_task_assignment(assignment_id,'accept',null);
   if idx in(1,10) then perform public.respond_task_assignment(assignment_id,'complete','DEMO — Execução validada.');
   else perform public.move_task_atomic(task_id,'in-progress',idx); end if;
   insert into public.time_logs(task_id,user_id,minutes,log_date,description,is_billable)
   values(task_id,executor_id,case when idx in(1,2) then 90 else 120 end,current_date,'DEMO — Execução de atividade do piloto.',true) returning id into log_id;
   perform set_config('request.jwt.claims',jsonb_build_object('sub',owner_id,'role','authenticated')::text,true);
   if idx in(1,2,10) then perform public.review_billing_time_log(log_id,true,owner_id); end if;
  end if;
 end loop;
 insert into public.task_dependencies(task_id,depends_on_task_id) values(tasks[6],tasks[4]);
 perform public.add_task_approval_step(tasks[4],1,owner_id);
 perform public.move_task_atomic(tasks[4],'review',0);
 perform public.capture_project_baseline(boards[1],'DEMO — Escopo inicial de implantação');
 invoice_id:=public.generate_billing_invoice(clients[1],boards[1],owner_id,current_date,current_date,current_date+15);
 invoice_id:=public.generate_billing_invoice(clients[2],boards[2],owner_id,current_date,current_date,current_date+15);
 perform public.update_billing_invoice_status(invoice_id,'issued');
 insert into public.recurring_task_rules(board_id,title,cadence,next_run_at,assigned_to,created_by,estimated_minutes,due_after_days,max_occurrences)
 values(boards[2],'DEMO — Revisar indicadores de atendimento semanal','weekly',now()+interval '1 day',people[1],owner_id,60,2,4);
end $$;
commit;
select b.name,(select count(*) from public.tasks t where t.board_id=b.id) tasks
from public.boards b where b.name in('DEMO — Horizonte Logística | Implantação','DEMO — Clínica Aurora | Atendimento');
