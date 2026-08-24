-- Remove o recurso descontinuado de formulários públicos e todos os seus registros.
-- Execute este script uma única vez no SQL Editor do Supabase, após publicar esta versão.
drop table if exists public.intake_forms;
