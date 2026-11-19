-- O workflow da tarefa usa in_progress; accepted pertence às atribuições.
do $$ declare definition text; begin
 definition:=pg_get_functiondef('private.decide_task_approval_impl(uuid,text,text)'::regprocedure);
 if position('else ''accepted'' end' in definition)=0 then raise exception 'Versão inesperada da decisão de aprovação.'; end if;
 execute replace(definition,'else ''accepted'' end','else ''in_progress'' end');
end $$;
