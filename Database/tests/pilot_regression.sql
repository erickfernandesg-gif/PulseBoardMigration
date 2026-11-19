-- Regressão transacional: não deixa dados de teste na base.
begin;
select set_config('pilot.admin',(select id::text from public.profiles where role='admin' and is_active order by id limit 1),true);
select set_config('pilot.executor',(select id::text from public.profiles where role='user' and is_active order by id limit 1),true);
select set_config('pilot.outsider',(select id::text from public.profiles where role='user' and is_active and id::text<>current_setting('pilot.executor') order by id limit 1),true);
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.admin'),'role','authenticated')::text,true);
do $$ declare b uuid; t uuid; a uuid; c uuid; begin
insert into public.boards(name,owner_id,settings) values('AUDIT ROLLBACK',current_setting('pilot.admin')::uuid,
'[{"id":"todo","title":"A fazer"},{"id":"in-progress","title":"Execução"},{"id":"review","title":"Revisão","requires_approval":true},{"id":"done","title":"Concluído"}]') returning id into b;
insert into public.tasks(board_id,title,assigned_to,created_by,accountable_owner_id) values(b,'AUDIT tarefa',current_setting('pilot.executor')::uuid,current_setting('pilot.admin')::uuid,current_setting('pilot.admin')::uuid) returning id into t;
insert into public.task_comments(task_id,user_id,content) values(t,current_setting('pilot.executor')::uuid,'AUDIT comentário restrito');
insert into public.tasks(board_id,title,assigned_to,created_by,accountable_owner_id) values(b,'AUDIT aprovação',current_setting('pilot.executor')::uuid,current_setting('pilot.admin')::uuid,current_setting('pilot.admin')::uuid) returning id into a;
insert into public.clients(name) values('AUDIT cliente') returning id into c;
insert into public.client_contracts(client_id,board_id,name,contract_type,billing_rate,starts_on) values(c,b,'AUDIT contrato','hourly',150,current_date-30);
update public.tasks set client_id=c where id=t;
perform set_config('pilot.board',b::text,true); perform set_config('pilot.task',t::text,true); perform set_config('pilot.approval_task',a::text,true); perform set_config('pilot.client',c::text,true);
end $$;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.outsider'),'role','authenticated')::text,true);
set local role authenticated;
do $$ declare denied boolean:=false; begin
if exists(select 1 from public.task_comments where content='AUDIT comentário restrito') then raise exception 'Vazamento de comentário'; end if;
begin insert into public.task_followers(task_id,user_id) values(current_setting('pilot.task')::uuid,auth.uid()); exception when insufficient_privilege then denied:=true; end;
if not denied then raise exception 'Autoinscrição não autorizada permitida'; end if;
end $$;
reset role;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.executor'),'role','authenticated')::text,true);
set local role authenticated;
select set_config('pilot.assignment',(select id::text from public.task_assignments where task_id=current_setting('pilot.task')::uuid and status='pending' limit 1),true);
select public.respond_task_assignment(current_setting('pilot.assignment')::uuid,'accept',null);
insert into public.task_checklists(task_id,title,is_completed) values(current_setting('pilot.task')::uuid,'AUDIT checklist',false);
do $$ declare denied boolean:=false; begin
begin perform public.respond_task_assignment(current_setting('pilot.assignment')::uuid,'complete',null);
exception when others then if position('checklist' in sqlerrm)>0 then denied:=true; else raise; end if; end;
if not denied then raise exception 'Conclusão com checklist pendente permitida'; end if;
end $$;
update public.task_checklists set is_completed=true where task_id=current_setting('pilot.task')::uuid;
select public.respond_task_assignment(current_setting('pilot.assignment')::uuid,'complete',null);
insert into public.time_logs(task_id,user_id,minutes,is_billable,description) values(current_setting('pilot.task')::uuid,auth.uid(),90,true,'AUDIT execução');
reset role;
select set_config('pilot.log',(select id::text from public.time_logs where task_id=current_setting('pilot.task')::uuid),true);
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.admin'),'role','authenticated')::text,true);
set local role authenticated;
select set_config('pilot.step',public.add_task_approval_step(current_setting('pilot.approval_task')::uuid,1,current_setting('pilot.outsider')::uuid)::text,true);
select public.move_task_atomic(current_setting('pilot.approval_task')::uuid,'review',0);
reset role;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.executor'),'role','authenticated')::text,true);
set local role authenticated;
do $$ declare denied boolean:=false; begin
begin update public.task_approval_steps set status='approved' where id=current_setting('pilot.step')::uuid; exception when insufficient_privilege then denied:=true; end;
if not denied then raise exception 'Atualização direta de aprovação permitida'; end if;
denied:=false;
begin perform public.decide_task_approval(current_setting('pilot.step')::uuid,'approve',null); exception when insufficient_privilege then denied:=true; end;
if not denied then raise exception 'Executor aprovou a própria etapa'; end if;
end $$;
reset role;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.outsider'),'role','authenticated')::text,true);
set local role authenticated;
select public.decide_task_approval(current_setting('pilot.step')::uuid,'approve','AUDIT aceite');
reset role;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.admin'),'role','authenticated')::text,true);
set local role authenticated;
select public.review_billing_time_log(current_setting('pilot.log')::uuid,true,auth.uid());
select set_config('pilot.invoice',public.generate_billing_invoice(current_setting('pilot.client')::uuid,current_setting('pilot.board')::uuid,auth.uid(),current_date-1,current_date,current_date+10)::text,true);
do $$ begin if not exists(select 1 from public.billing_invoices where id=current_setting('pilot.invoice')::uuid and total=225) then raise exception 'Valor da fatura incorreto'; end if; end $$;
select public.update_billing_invoice_status(current_setting('pilot.invoice')::uuid,'cancelled');
do $$ begin if not exists(select 1 from public.time_logs where id=current_setting('pilot.log')::uuid and invoice_id is null and billing_status='unbilled') then raise exception 'Cancelamento não liberou horas'; end if; end $$;
select set_config('pilot.invoice',public.generate_billing_invoice(current_setting('pilot.client')::uuid,current_setting('pilot.board')::uuid,auth.uid(),current_date-1,current_date,current_date+10)::text,true);
select public.update_billing_invoice_status(current_setting('pilot.invoice')::uuid,'issued');
select public.update_billing_invoice_status(current_setting('pilot.invoice')::uuid,'paid');
do $$ declare denied boolean:=false; begin
begin perform public.update_billing_invoice_status(current_setting('pilot.invoice')::uuid,'cancelled'); exception when others then denied:=true; end;
if not denied then raise exception 'Cancelamento de fatura paga permitido'; end if;
end $$;
select public.archive_task(current_setting('pilot.task')::uuid);
do $$ declare denied boolean:=false; begin
begin perform public.permanently_delete_archived_task(current_setting('pilot.task')::uuid); exception when foreign_key_violation then denied:=true; end;
if not denied then raise exception 'Exclusão de tarefa faturada permitida'; end if;
end $$;
select public.restore_task(current_setting('pilot.task')::uuid);
-- Concorrência no Gantt e baseline.
select set_config('pilot.version',(select row_version::text from public.tasks where id=current_setting('pilot.approval_task')::uuid),true);
select public.update_task_schedule_atomic(current_setting('pilot.approval_task')::uuid,current_setting('pilot.version')::bigint,current_date,current_date+2);
do $$ declare denied boolean:=false; begin
begin perform public.update_task_schedule_atomic(current_setting('pilot.approval_task')::uuid,current_setting('pilot.version')::bigint,current_date,current_date+3); exception when serialization_failure then denied:=true; end;
if not denied then raise exception 'Versão antiga aceita no Gantt'; end if;
end $$;
select public.capture_project_baseline(current_setting('pilot.board')::uuid,'AUDIT baseline');
reset role;
-- Dependências e handoff com aceite final.
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.admin'),'role','authenticated')::text,true);
do $$ declare p uuid; d uuid; begin
insert into public.tasks(board_id,title,created_by,accountable_owner_id) values(current_setting('pilot.board')::uuid,'AUDIT pré-requisito',auth.uid(),auth.uid()) returning id into p;
insert into public.tasks(board_id,title,assigned_to,created_by,accountable_owner_id) values(current_setting('pilot.board')::uuid,'AUDIT dependente',current_setting('pilot.executor')::uuid,auth.uid(),auth.uid()) returning id into d;
insert into public.task_dependencies(task_id,depends_on_task_id) values(d,p);
perform set_config('pilot.prerequisite',p::text,true); perform set_config('pilot.dependent',d::text,true);
end $$;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.executor'),'role','authenticated')::text,true);
set local role authenticated;
select set_config('pilot.dependent_assignment',(select id::text from public.task_assignments where task_id=current_setting('pilot.dependent')::uuid and status='pending'),true);
do $$ declare denied boolean:=false; begin
begin perform public.respond_task_assignment(current_setting('pilot.dependent_assignment')::uuid,'accept',null); exception when others then if position('dependências' in sqlerrm)>0 then denied:=true; else raise; end if; end;
if not denied then raise exception 'Pré-requisito pendente ignorado'; end if;
end $$;
reset role;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.admin'),'role','authenticated')::text,true);
set local role authenticated;
select public.move_task_atomic(current_setting('pilot.prerequisite')::uuid,'done',0);
reset role;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.executor'),'role','authenticated')::text,true);
set local role authenticated;
select public.respond_task_assignment(current_setting('pilot.dependent_assignment')::uuid,'accept',null);
select public.return_task_with_question(current_setting('pilot.dependent')::uuid,current_setting('pilot.admin')::uuid,'AUDIT dúvida');
reset role;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.admin'),'role','authenticated')::text,true);
set local role authenticated;
select set_config('pilot.final_assignment',public.handoff_task(current_setting('pilot.dependent')::uuid,current_setting('pilot.executor')::uuid,'in-progress',null,60,'AUDIT resposta','Validado',true,auth.uid())::text,true);
reset role;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.executor'),'role','authenticated')::text,true);
set local role authenticated;
select public.respond_task_assignment(current_setting('pilot.final_assignment')::uuid,'accept',null);
select public.respond_task_assignment(current_setting('pilot.final_assignment')::uuid,'complete',null);
reset role;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.admin'),'role','authenticated')::text,true);
set local role authenticated;
select public.review_task(current_setting('pilot.dependent')::uuid,'approve','AUDIT entrega aprovada');
reset role;
-- Recorrência executada como cron, sem JWT de usuário.
do $$ declare r uuid; begin
insert into public.recurring_task_rules(board_id,title,cadence,next_run_at,assigned_to,created_by,estimated_minutes,due_after_days)
values(current_setting('pilot.board')::uuid,'AUDIT recorrência','weekly',now()-interval '1 minute',current_setting('pilot.executor')::uuid,current_setting('pilot.admin')::uuid,60,2) returning id into r;
perform set_config('pilot.recurring',r::text,true);
end $$;
select set_config('request.jwt.claims','{}',true);
select private.generate_recurring_tasks();
select private.generate_recurring_tasks();
do $$ begin
if (select count(*) from public.tasks where recurring_rule_id=current_setting('pilot.recurring')::uuid)<>1 then raise exception 'Recorrência duplicada ou ausente'; end if;
if not exists(select 1 from public.task_assignments a join public.tasks t on t.id=a.task_id where t.recurring_rule_id=current_setting('pilot.recurring')::uuid and a.status='pending') then raise exception 'Recorrência não gerou atribuição'; end if;
end $$;
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.admin'),'role','authenticated')::text,true);
insert into public.notification_preferences(user_id,in_app,due_reminders) values(current_setting('pilot.executor')::uuid,true,false) on conflict(user_id) do update set in_app=true,due_reminders=false;
update public.tasks set due_date=now()-interval '2 hours' where id=current_setting('pilot.approval_task')::uuid;
delete from public.notifications where task_id=current_setting('pilot.approval_task')::uuid and type in ('task_overdue','task_due_soon');
select set_config('request.jwt.claims',jsonb_build_object('sub',current_setting('pilot.executor'),'role','authenticated')::text,true);
set local role authenticated;
select public.ensure_due_notifications();
do $$ begin if exists(select 1 from public.notifications where task_id=current_setting('pilot.approval_task')::uuid and recipient_id=auth.uid() and type in ('task_overdue','task_due_soon')) then raise exception 'Preferência ignorada'; end if; end $$;
reset role;
select jsonb_build_object('passed',array['isolamento de comentários','bloqueio de autoinscrição','aceite de atribuição','bloqueio por checklist','conclusão de tarefa','bloqueio de aprovação direta','aprovação autorizada','apontamento e revisão de horas','cálculo de fatura','cancelamento e liberação de horas','emissão e pagamento','imutabilidade de fatura paga','arquivo e restauração','preservação financeira','preferência de prazo','concorrência no Gantt','baseline','bloqueio por dependência','devolução com dúvida','handoff e aceite final','recorrência idempotente com atribuição']) result;
rollback;
