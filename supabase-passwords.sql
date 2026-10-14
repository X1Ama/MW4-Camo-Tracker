-- Keeps a viewable copy of passwords set from the Admin panel.
-- Run once in Supabase > SQL Editor, after supabase.sql and supabase-admin.sql.
--
-- IMPORTANT: this stores passwords as plain text. Supabase itself only keeps a one-way hash, which cannot be
-- shown again. Only owners (all users) and admins (their own clients) can read the copies, and only passwords
-- set from the Admin panel after this script ran are stored. Skip this file if you do not want that.

create table if not exists public.password_notes (
  user_id    uuid primary key references public.profiles(id) on delete cascade,
  password   text not null,
  updated_at timestamptz not null default now()
);

alter table public.password_notes enable row level security;

drop policy if exists password_notes_read on public.password_notes;
create policy password_notes_read on public.password_notes for select to authenticated
  using (
    public.can_manage(user_id)
    and (public.my_role() = 'owner'
         or (select role from public.profiles where id = user_id) = 'client')
  );
-- No insert/update/delete policies: only the functions below can write.

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

  insert into public.profiles (id, email, display_name, role)
  values (v_id, v_email, coalesce(p_display_name, ''), p_role)
  on conflict (id) do update set role = excluded.role, display_name = excluded.display_name;

  insert into public.password_notes (user_id, password) values (v_id, p_password)
  on conflict (user_id) do update set password = excluded.password, updated_at = now();

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
  insert into public.password_notes (user_id, password) values (p_user, p_password)
  on conflict (user_id) do update set password = excluded.password, updated_at = now();
end $$;
