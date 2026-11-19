-- O gatilho já sucede uma escrita autorizada. Revalidar o ator após a troca
-- de responsável bloqueava handoffs legítimos e processos internos sem sessão.
-- A entrada pública mantém a autorização; o executor interno deixa de ser público.
create or replace function private.activate_task_approval(p_task_id uuid,p_reset boolean default false)
returns void language plpgsql security definer set search_path='' as $$
declare req boolean; step_id uuid; approver uuid; effective uuid; b uuid; task_title text;
begin
 select coalesce((c->>'requires_approval')::boolean,false),t.board_id,t.title into req,b,task_title
 from public.tasks t join public.boards board on board.id=t.board_id
 left join lateral jsonb_array_elements(coalesce(board.settings,'[]'::jsonb)) c on c->>'id'=t.status where t.id=p_task_id limit 1;
 if not coalesce(req,false) then return; end if;
 if p_reset then update public.task_approval_steps set status='waiting',decision_by=null,decision_note=null,decided_at=null where task_id=p_task_id; end if;
 if exists(select 1 from public.task_approval_steps where task_id=p_task_id and status='pending') then return; end if;
 select id,approver_id into step_id,approver from public.task_approval_steps where task_id=p_task_id and status='waiting' order by sequence limit 1 for update;
 if step_id is null then return; end if;
 update public.task_approval_steps set status='pending' where id=step_id;
 update public.tasks set workflow_state='waiting_review' where id=p_task_id;
 select coalesce((select d.substitute_id from public.approval_delegations d where d.delegator_id=approver and d.is_active
 and current_date between d.starts_on and d.ends_on order by d.created_at desc limit 1),approver) into effective;
 insert into public.notifications(recipient_id,user_id,actor_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
 values(effective,effective,(select auth.uid()),p_task_id,b,'approval_required','Aprovação pendente',task_title,'/Boards/Details/'||b,'high','approval:'||step_id)
 on conflict(deduplication_key) where deduplication_key is not null do update
 set recipient_id=excluded.recipient_id,user_id=excluded.user_id,actor_id=excluded.actor_id,message=excluded.message,read_at=null,archived_at=null,created_at=now();
end $$;
revoke all on function private.activate_task_approval(uuid,boolean) from public,anon,authenticated,service_role;
create or replace function public.activate_task_approval_if_required(p_task_id uuid)
returns void language plpgsql security definer set search_path='' as $$
begin
 if not private.can_edit_task(p_task_id) then raise exception 'Sem permissão para configurar a aprovação.' using errcode='42501'; end if;
 perform private.activate_task_approval(p_task_id,false);
end $$;
revoke all on function public.activate_task_approval_if_required(uuid) from public,anon;
grant execute on function public.activate_task_approval_if_required(uuid) to authenticated;
