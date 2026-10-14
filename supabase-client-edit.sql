-- Let clients edit their own progress (ticks, levels, weapon types, extra weapons).
-- Run once in Supabase > SQL Editor. Staff keep their access to the clients they manage.
do $$
declare t text;
begin
  foreach t in array array['ticks','levels','classes','custom_weapons'] loop
    execute format('drop policy if exists %I_write on public.%I', t, t);
    execute format($p$create policy %I_write on public.%I for all to authenticated
                      using (client_id = auth.uid() or public.can_manage(client_id))
                      with check (client_id = auth.uid() or public.can_manage(client_id))$p$, t, t);
  end loop;
end $$;