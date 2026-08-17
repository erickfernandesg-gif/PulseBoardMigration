-- Integração e confiabilidade da Central de Operações. Idempotente.

-- Normaliza regras criadas pela versão anterior e impede novas combinações sem executor.
update public.automations set action_type = 'assign_user' where action_type = 'assign_auto';
delete from public.automations
where trigger_type not in ('status_change','priority_change','assignment_change')
   or action_type not in ('notify_manager','assign_user','move_status','set_priority','set_due_days');
alter table public.automations drop constraint if exists automations_trigger_type_check;
alter table public.automations add constraint automations_trigger_type_check
  check (trigger_type in ('status_change','priority_change','assignment_change'));
alter table public.automations drop constraint if exists automations_action_type_check;
alter table public.automations add constraint automations_action_type_check
  check (action_type in ('notify_manager','assign_user','move_status','set_priority','set_due_days'));
drop policy if exists automations_manage on public.automations;
create policy automations_manage on public.automations for all to authenticated
using (case when board_id is null then public.is_manager() else private.can_edit_board(board_id) end)
with check (case when board_id is null then public.is_manager() else private.can_edit_board(board_id) end);

-- Detecta o aprovador efetivo, incluindo substituições vigentes.
create or replace function private.can_review_approval(target_step_id uuid)
returns boolean language sql stable security definer set search_path='' as $$
  select (select auth.uid()) is not null and exists (
    select 1
    from public.task_approval_steps s
    where s.id=target_step_id and (
      s.approver_id=(select auth.uid())
      or exists (
        select 1 from public.approval_delegations d
        where d.delegator_id=s.approver_id and d.substitute_id=(select auth.uid())
          and d.is_active and current_date between d.starts_on and d.ends_on
      )
      or public.is_manager()
    )
  );
$$;
revoke execute on function private.can_review_approval(uuid) from public,anon;
grant execute on function private.can_review_approval(uuid) to authenticated,service_role;

create or replace function private.is_task_approval_participant(target_task_id uuid)
returns boolean language sql stable security definer set search_path='' as $$
  select (select auth.uid()) is not null and exists (
    select 1 from public.task_approval_steps s
    where s.task_id=target_task_id and (
      s.approver_id=(select auth.uid())
      or exists (
        select 1 from public.approval_delegations d
        where d.delegator_id=s.approver_id and d.substitute_id=(select auth.uid())
          and d.is_active and current_date between d.starts_on and d.ends_on
      )
    )
  );
$$;
revoke execute on function private.is_task_approval_participant(uuid) from public,anon;
grant execute on function private.is_task_approval_participant(uuid) to authenticated,service_role;

drop policy if exists boards_read on public.boards;
create policy boards_read on public.boards for select to authenticated using (
  (select private.can_read_board(id)) or exists (
    select 1 from public.tasks t where t.board_id=boards.id and (select private.is_task_approval_participant(t.id))
  )
);
drop policy if exists tasks_read on public.tasks;
create policy tasks_read on public.tasks for select to authenticated using (
  (select private.can_read_task(id)) or (select private.is_task_approval_participant(id))
);

drop policy if exists approval_steps_read on public.task_approval_steps;
create policy approval_steps_read on public.task_approval_steps for select to authenticated
using ((select private.can_read_task(task_id)) or (select private.can_review_approval(id)) or (select private.is_task_approval_participant(task_id)));

-- Ativa a primeira etapa e reutiliza o fluxo ao entrar novamente em uma coluna de aprovação.
create or replace function private.activate_task_approval(p_task_id uuid,p_reset boolean default false)
returns void language plpgsql security definer set search_path='' as $$
declare req boolean;step_id uuid;approver uuid;effective uuid;b uuid;task_title text;
begin
 if not private.can_edit_task(p_task_id) then raise exception 'Sem permissão para configurar a aprovação.' using errcode='42501';end if;
 select coalesce((c->>'requires_approval')::boolean,false),t.board_id,t.title
 into req,b,task_title
 from public.tasks t join public.boards board on board.id=t.board_id
 left join lateral jsonb_array_elements(coalesce(board.settings,'[]'::jsonb)) c on c->>'id'=t.status
 where t.id=p_task_id limit 1;
 if not coalesce(req,false) then return;end if;
 if p_reset then
  update public.task_approval_steps set status='waiting',decision_by=null,decision_note=null,decided_at=null where task_id=p_task_id;
 end if;
 if exists(select 1 from public.task_approval_steps where task_id=p_task_id and status='pending') then return;end if;
 select id,approver_id into step_id,approver from public.task_approval_steps
 where task_id=p_task_id and status='waiting' order by sequence limit 1 for update;
 if step_id is null then return;end if;
 update public.task_approval_steps set status='pending' where id=step_id;
 update public.tasks set workflow_state='waiting_review' where id=p_task_id;
 select coalesce((select d.substitute_id from public.approval_delegations d
   where d.delegator_id=approver and d.is_active and current_date between d.starts_on and d.ends_on
   order by d.created_at desc limit 1),approver) into effective;
 insert into public.notifications(recipient_id,user_id,actor_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
 values(effective,effective,(select auth.uid()),p_task_id,b,'approval_required','Aprovação pendente',task_title,'/Boards/Details/'||b,'high','approval:'||step_id)
 on conflict(deduplication_key) where deduplication_key is not null do update
 set recipient_id=excluded.recipient_id,user_id=excluded.user_id,actor_id=excluded.actor_id,message=excluded.message,
     read_at=null,archived_at=null,created_at=now();
end $$;
revoke execute on function private.activate_task_approval(uuid,boolean) from public,anon;
grant execute on function private.activate_task_approval(uuid,boolean) to authenticated,service_role;

create or replace function public.activate_task_approval_if_required(p_task_id uuid)
returns void language sql security invoker set search_path='' as $$
  select private.activate_task_approval(p_task_id,false);
$$;
revoke execute on function public.activate_task_approval_if_required(uuid) from public,anon;
grant execute on function public.activate_task_approval_if_required(uuid) to authenticated;

create or replace function private.start_required_approval() returns trigger
language plpgsql security definer set search_path='' as $$
begin
 if new.status is distinct from old.status then perform private.activate_task_approval(new.id,true);end if;
 return new;
end $$;

-- A decisão passa por uma rotina privada privilegiada, mas valida rigorosamente o usuário efetivo.
create or replace function private.decide_task_approval_impl(p_step_id uuid,p_decision text,p_note text default null)
returns void language plpgsql security definer set search_path='' as $$
declare s public.task_approval_steps;a uuid:=(select auth.uid());effective uuid;nxt record;b uuid;task_title text;
begin
 if a is null then raise exception 'Autenticação necessária.' using errcode='42501';end if;
 select * into s from public.task_approval_steps where id=p_step_id for update;
 if s.id is null or s.status<>'pending' or p_decision not in('approve','reject') then raise exception 'Etapa inválida.';end if;
 select coalesce((select d.substitute_id from public.approval_delegations d where d.delegator_id=s.approver_id and d.is_active and current_date between d.starts_on and d.ends_on order by d.created_at desc limit 1),s.approver_id) into effective;
 if a<>effective and not public.is_manager() then raise exception 'Aprovação pertence a outro usuário.' using errcode='42501';end if;
 update public.task_approval_steps set status=case when p_decision='approve' then 'approved' else 'rejected' end,
  decision_by=a,decision_note=nullif(trim(p_note),''),decided_at=now() where id=p_step_id;
 select board_id,title into b,task_title from public.tasks where id=s.task_id;
 if p_decision='reject' then
  update public.tasks set workflow_state='changes_requested',is_blocked=true,blocker_reason=coalesce(nullif(trim(p_note),''),'Aprovação rejeitada') where id=s.task_id;
 else
  select id,approver_id,sequence into nxt from public.task_approval_steps where task_id=s.task_id and sequence>s.sequence and status='waiting' order by sequence limit 1 for update;
  if nxt.id is not null then
   update public.task_approval_steps set status='pending' where id=nxt.id;
   select coalesce((select d.substitute_id from public.approval_delegations d where d.delegator_id=nxt.approver_id and d.is_active and current_date between d.starts_on and d.ends_on order by d.created_at desc limit 1),nxt.approver_id) into effective;
   insert into public.notifications(recipient_id,user_id,actor_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
   values(effective,effective,a,s.task_id,b,'approval_required','Aprovação pendente',task_title,'/Boards/Details/'||b,'high','approval:'||nxt.id)
   on conflict(deduplication_key) where deduplication_key is not null do update
   set recipient_id=excluded.recipient_id,user_id=excluded.user_id,actor_id=excluded.actor_id,read_at=null,archived_at=null,created_at=now();
  else
   update public.tasks set workflow_state=case when status='done' then 'done' else 'accepted' end,is_blocked=false,blocker_reason=null where id=s.task_id;
  end if;
 end if;
 insert into public.activity_log(task_id,board_id,user_id,action,details)
 values(s.task_id,b,a,'approval_decided',jsonb_build_object('sequence',s.sequence,'decision',p_decision,'note',p_note));
end $$;
revoke execute on function private.decide_task_approval_impl(uuid,text,text) from public,anon;
grant execute on function private.decide_task_approval_impl(uuid,text,text) to authenticated,service_role;

create or replace function public.decide_task_approval(p_step_id uuid,p_decision text,p_note text default null)
returns void language sql security invoker set search_path='' as $$
 select private.decide_task_approval_impl(p_step_id,p_decision,p_note);
$$;
revoke execute on function public.decide_task_approval(uuid,text,text) from public,anon;
grant execute on function public.decide_task_approval(uuid,text,text) to authenticated;

-- Impede ciclos diretos ou transitivos nas dependências.
create or replace function private.prevent_task_dependency_cycle() returns trigger
language plpgsql security definer set search_path='' as $$
begin
 if exists (
   with recursive chain(task_id) as (
     select new.depends_on_task_id
     union
     select d.depends_on_task_id from public.task_dependencies d join chain c on d.task_id=c.task_id
     where tg_op='INSERT' or d.id<>new.id
   ) select 1 from chain where task_id=new.task_id
 ) then raise exception 'A dependência criaria um ciclo entre tarefas.' using errcode='23514';end if;
 return new;
end $$;
drop trigger if exists prevent_task_dependency_cycle on public.task_dependencies;
create trigger prevent_task_dependency_cycle before insert or update of task_id,depends_on_task_id
on public.task_dependencies for each row execute function private.prevent_task_dependency_cycle();

-- Automações inválidas são ignoradas com segurança, sem interromper a edição da tarefa.
create or replace function public.execute_task_automations() returns trigger
language plpgsql security definer set search_path='' as $$
declare r record;target uuid;days integer;
begin
 if pg_trigger_depth()>1 then return new;end if;
 for r in select * from public.automations a where a.is_active and (a.board_id is null or a.board_id=new.board_id)
   and ((a.trigger_type='status_change' and new.status is distinct from old.status and a.trigger_value=new.status)
     or (a.trigger_type='priority_change' and new.priority is distinct from old.priority and a.trigger_value=new.priority)
     or (a.trigger_type='assignment_change' and new.assigned_to is distinct from old.assigned_to)) loop
  if r.action_type='assign_user' then
   begin target:=r.action_payload::uuid;exception when invalid_text_representation then target:=null;end;
   if exists(select 1 from public.profiles p where p.id=target and p.is_active) then new.assigned_to=target;end if;
  elsif r.action_type='move_status' then
   if exists(select 1 from public.boards b cross join lateral jsonb_array_elements(coalesce(b.settings,'[]'::jsonb)) c where b.id=new.board_id and c->>'id'=r.action_payload) then new.status=r.action_payload;end if;
  elsif r.action_type='set_priority' and r.action_payload in('low','medium','high','critical') then new.priority=r.action_payload;
  elsif r.action_type='set_due_days' then begin days:=r.action_payload::integer;if days between 0 and 3650 then new.due_date=current_date+days;end if;exception when invalid_text_representation then null;end;
  elsif r.action_type='notify_manager' then
   select owner_id into target from public.boards where id=new.board_id;
   insert into public.notifications(recipient_id,user_id,actor_id,task_id,board_id,type,title,message,action_url,priority,deduplication_key)
   values(target,target,(select auth.uid()),new.id,new.board_id,'automation','Automação: '||r.title,new.title,'/Boards/Details/'||new.board_id,'normal','automation:'||r.id||':'||new.id||':'||to_char(now(),'YYYYMMDDHH24'))
   on conflict(deduplication_key) where deduplication_key is not null do nothing;
  end if;
  if (select auth.uid()) is not null then insert into public.activity_log(task_id,board_id,user_id,action,details) values(new.id,new.board_id,(select auth.uid()),'automation_fired',jsonb_build_object('automation',r.title,'action',r.action_type));end if;
 end loop;return new;
end $$;
revoke execute on function public.execute_task_automations() from public,anon,authenticated;

notify pgrst,'reload schema';
