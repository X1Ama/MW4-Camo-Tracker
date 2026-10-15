-- Admin panel functions. Run once in Supabase > SQL Editor, AFTER supabase.sql.
-- They let the app's Admin Panel create users, reset passwords, change roles, assign clients and delete users.
-- Every function checks who is calling, so a client calling them directly is refused.

create extension if not exists pgcrypto with schema extensions;

create or replace function public.my_role() returns text
language sql stable security definer set search_path = public as $$
  select role from public.profiles where id = auth.uid();
$$;

-- Create a user. Owners may create any role. Admins may only create clients, who are assigned to them.
create or replace function public.admin_create_user(
  p_username text, p_password text, p_display_name text default '', p_role text default 'client')
returns uuid
language plpgsql security definer set search_path = public, extensions, auth as $$
declare
  v_me text := public.my_role();
  v_id uuid := gen_random_uuid();
  v_email text;
begin
  if v_me not in ('owner','admin') then raise exception 'Not allowed.'; end if;
  if p_role not in ('client','admin','owner') then raise exception 'Unknown role.'; end if;
  if v_me = 'admin' and p_role <> 'client' then raise exception 'Admins can only create clients.'; end if;
  if p_username !~ '^[A-Za-z0-9._-]{2,32}$' then
    raise exception 'User names are 2-32 letters, numbers, dots, dashes or underscores.';
  end if;
  if length(p_password) < 6 then raise exception 'Password must be at least 6 characters.'; end if;

  v_email := lower(p_username) || '@tracker.local';
  if exists (select 1 from auth.users where email = v_email) then
    raise exception 'That user name is already taken.';
  end if;

  insert into auth.users (
    instance_id, id, aud, role, email, encrypted_password, email_confirmed_at,
    raw_app_meta_data, raw_user_meta_data, created_at, updated_at,
    confirmation_token, email_change, email_change_token_new, recovery_token)
  values (
    '00000000-0000-0000-0000-000000000000', v_id, 'authenticated', 'authenticated', v_email,
    crypt(p_password, gen_salt('bf')), now(),
    '{"provider":"email","providers":["email"]}'::jsonb,
    jsonb_build_object('display_name', coalesce(p_display_name, '')), now(), now(),
    '', '', '', '');

  insert into auth.identities (id, user_id, identity_data, provider, provider_id, last_sign_in_at, created_at, updated_at)
  values (gen_random_uuid(), v_id,
    jsonb_build_object('sub', v_id::text, 'email', v_email, 'email_verified', true),
    'email', v_id::text, now(), now(), now());

  -- the sign-up trigger already created the profile; set role and name
  insert into public.profiles (id, email, display_name, role)
  values (v_id, v_email, coalesce(p_display_name, ''), p_role)
  on conflict (id) do update set role = excluded.role, display_name = excluded.display_name;

  if v_me = 'admin' then
    insert into public.admin_clients (admin_id, client_id) values (auth.uid(), v_id) on conflict do nothing;
  end if;
  return v_id;
end $$;

create or replace function public.admin_set_password(p_user uuid, p_password text) returns void
language plpgsql security definer set search_path = public, extensions, auth as $$
begin
  if public.my_role() not in ('owner','admin') or not public.can_manage(p_user) then raise exception 'Not allowed.'; end if;
  if public.my_role() = 'admin' and (select role from public.profiles where id = p_user) <> 'client' then
    raise exception 'Not allowed.';
  end if;
  if length(p_password) < 6 then raise exception 'Password must be at least 6 characters.'; end if;
  update auth.users set encrypted_password = crypt(p_password, gen_salt('bf')), updated_at = now() where id = p_user;
end $$;

create or replace function public.admin_set_display_name(p_user uuid, p_name text) returns void
language plpgsql security definer set search_path = public as $$
begin
  if public.my_role() not in ('owner','admin') or not public.can_manage(p_user) then raise exception 'Not allowed.'; end if;
  -- an admin (Grinder) may only rename clients, never another admin or an owner
  if public.my_role() = 'admin' and (select role from public.profiles where id = p_user) <> 'client' then
    raise exception 'Not allowed.';
  end if;
  update public.profiles set display_name = coalesce(p_name, '') where id = p_user;
end $$;

create or replace function public.admin_set_role(p_user uuid, p_role text) returns void
language plpgsql security definer set search_path = public as $$
begin
  if public.my_role() <> 'owner' then raise exception 'Only an owner can change roles.'; end if;
  if p_role not in ('client','admin','owner') then raise exception 'Unknown role.'; end if;
  if p_user = auth.uid() then raise exception 'You cannot change your own role.'; end if;
  update public.profiles set role = p_role where id = p_user;
end $$;

create or replace function public.admin_assign(p_admin uuid, p_client uuid, p_on boolean) returns void
language plpgsql security definer set search_path = public as $$
begin
  if public.my_role() <> 'owner' then raise exception 'Only an owner can assign clients.'; end if;
  if p_on then
    insert into public.admin_clients (admin_id, client_id) values (p_admin, p_client) on conflict do nothing;
  else
    delete from public.admin_clients where admin_id = p_admin and client_id = p_client;
  end if;
end $$;

create or replace function public.admin_delete_user(p_user uuid) returns void
language plpgsql security definer set search_path = public, auth as $$
begin
  if p_user = auth.uid() then raise exception 'You cannot delete yourself.'; end if;
  if public.my_role() = 'owner' then
    null;
  elsif public.my_role() = 'admin' and public.can_manage(p_user)
        and (select role from public.profiles where id = p_user) = 'client' then
    null;
  else
    raise exception 'Not allowed.';
  end if;
  delete from auth.users where id = p_user;   -- cascades to the profile and all progress
end $$;

revoke all on function public.my_role() from public, anon;
revoke all on function public.admin_create_user(text,text,text,text) from public, anon;
revoke all on function public.admin_set_password(uuid,text) from public, anon;
revoke all on function public.admin_set_display_name(uuid,text) from public, anon;
revoke all on function public.admin_set_role(uuid,text) from public, anon;
revoke all on function public.admin_assign(uuid,uuid,boolean) from public, anon;
revoke all on function public.admin_delete_user(uuid) from public, anon;
grant execute on function public.my_role() to authenticated;
grant execute on function public.admin_create_user(text,text,text,text) to authenticated;
grant execute on function public.admin_set_password(uuid,text) to authenticated;
grant execute on function public.admin_set_display_name(uuid,text) to authenticated;
grant execute on function public.admin_set_role(uuid,text) to authenticated;
grant execute on function public.admin_assign(uuid,uuid,boolean) to authenticated;
grant execute on function public.admin_delete_user(uuid) to authenticated;
