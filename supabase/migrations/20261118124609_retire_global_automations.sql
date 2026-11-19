-- Preserva a definição legada, mas elimina seus efeitos em projetos alheios.
update public.automations set is_active=false where board_id is null and is_active;
alter table public.automations add constraint automations_active_board_required check(not is_active or board_id is not null);
do $$ declare definition text; begin
 definition:=pg_get_functiondef('public.execute_task_automations()'::regprocedure);
 if position('(a.board_id is null or a.board_id=new.board_id)' in definition)=0 then
  raise exception 'Executor de automações difere da versão auditada. Revise antes de aplicar.';
 end if;
 definition:=replace(definition,'(a.board_id is null or a.board_id=new.board_id)','a.board_id=new.board_id');
 execute definition;
end $$;
notify pgrst,'reload schema';
