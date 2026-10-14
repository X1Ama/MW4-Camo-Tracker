-- Mastery Camo Tracker: Supabase schema. Paste into Supabase > SQL Editor > Run (once).
--
-- Roles:
--   owner  sees and edits every client, and can assign admins to clients
--   admin  sees and edits only the clients assigned to them (many admins, many clients each)
--   client sees only their own progress (read-only in the app)

create table if not exists public.profiles (
  id           uuid primary key references auth.users(id) on delete cascade,
  email        text not null default '',
  display_name text not null default '',
  role         text not null default 'client' check (role in ('client','admin','owner')),
  created_at   timestamptz not null default now()
);

create table if not exists public.admin_clients (
  admin_id  uuid not null references public.profiles(id) on delete cascade,
  client_id uuid not null references public.profiles(id) on delete cascade,
  primary key (admin_id, client_id)
);

create table if not exists public.ticks (
  client_id uuid not null references public.profiles(id) on delete cascade,
  camo_id   text not null,
  at        timestamptz not null default now(),
  primary key (client_id, camo_id)
);

create table if not exists public.levels (
  client_id  uuid not null references public.profiles(id) on delete cascade,
  weapon_key text not null,
  level      int  not null default 0 check (level between 0 and 200),
  primary key (client_id, weapon_key)
);

create table if not exists public.classes (
  client_id  uuid not null references public.profiles(id) on delete cascade,
  weapon_key text not null,
  cls        text not null,
  primary key (client_id, weapon_key)
);

create table if not exists public.custom_weapons (
  id         uuid primary key default gen_random_uuid(),
  client_id  uuid not null references public.profiles(id) on delete cascade,
  name       text not null,
  cls        text not null default '',
  created_at timestamptz not null default now()
);

-- Every new sign-up gets a profile row (role 'client' until an owner promotes them).
create or replace function public.handle_new_user() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  insert into public.profiles (id, email, display_name)
  values (new.id, coalesce(new.email, ''), coalesce(new.raw_user_meta_data->>'display_name', ''))
  on conflict (id) do nothing;
  return new;
end $$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created after insert on auth.users
  for each row execute function public.handle_new_user();

-- Helpers (security definer so policies can use them without recursion)
create or replace function public.is_owner() returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.profiles where id = auth.uid() and role = 'owner');
$$;

create or replace function public.can_manage(target uuid) returns boolean
language sql stable security definer set search_path = public as $$
  select public.is_owner()
      or exists (select 1 from public.admin_clients
                 where admin_id = auth.uid() and client_id = target);
$$;

-- Row level security
alter table public.profiles       enable row level security;
alter table public.admin_clients  enable row level security;
alter table public.ticks          enable row level security;
alter table public.levels         enable row level security;
alter table public.classes        enable row level security;
alter table public.custom_weapons enable row level security;

drop policy if exists profiles_read   on public.profiles;
drop policy if exists profiles_update on public.profiles;
create policy profiles_read   on public.profiles for select to authenticated
  using (id = auth.uid() or public.can_manage(id));
create policy profiles_update on public.profiles for update to authenticated
  using (public.is_owner()) with check (public.is_owner());

drop policy if exists admin_clients_read  on public.admin_clients;
drop policy if exists admin_clients_write on public.admin_clients;
create policy admin_clients_read  on public.admin_clients for select to authenticated
  using (admin_id = auth.uid() or public.is_owner());
create policy admin_clients_write on public.admin_clients for all to authenticated
  using (public.is_owner()) with check (public.is_owner());

-- Progress tables: clients read their own rows; owners and assigned admins read and write.
-- To let clients tick their own camos, add "or client_id = auth.uid()" to the write policies.
do $$
declare t text;
begin
  foreach t in array array['ticks','levels','classes','custom_weapons'] loop
    execute format('drop policy if exists %I_read on public.%I', t, t);
    execute format('drop policy if exists %I_write on public.%I', t, t);
    execute format($p$create policy %I_read on public.%I for select to authenticated
                      using (client_id = auth.uid() or public.can_manage(client_id))$p$, t, t);
    execute format($p$create policy %I_write on public.%I for all to authenticated
                      using (public.can_manage(client_id)) with check (public.can_manage(client_id))$p$, t, t);
  end loop;
end $$;

-- After you sign up your own account, make yourself the owner (replace the email):
--   update public.profiles set role = 'owner' where email = 'you@example.com';
--
-- Make someone an admin, then give them clients:
--   update public.profiles set role = 'admin' where email = 'admin@example.com';
--   insert into public.admin_clients (admin_id, client_id)
--     select a.id, c.id from public.profiles a, public.profiles c
--     where a.email = 'admin@example.com' and c.email = 'client@example.com';
