-- Administração de perfis é exclusiva de administradores.
-- Alterações do próprio nome passam por uma função limitada, sem expor cargo/equipe.
drop policy if exists profiles_update on public.profiles;
drop policy if exists profiles_update_self on public.profiles;
create policy profiles_update_admin on public.profiles for update to authenticated
  using ((select public.is_admin()))
  with check ((select public.is_admin()));

create or replace function public.update_own_profile_name(p_full_name text)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
begin
  if (select auth.uid()) is null then
    raise exception 'Autenticação obrigatória';
  end if;
  if char_length(trim(coalesce(p_full_name, ''))) not between 2 and 160 then
    raise exception 'Informe um nome entre 2 e 160 caracteres';
  end if;
  update public.profiles
     set full_name = trim(p_full_name)
   where id = (select auth.uid());
  return found;
end;
$$;

revoke all on function public.update_own_profile_name(text) from public, anon;
grant execute on function public.update_own_profile_name(text) to authenticated;
