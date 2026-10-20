-- The widgets of the life-cycle tests: one table under row-level security, filtered by the space of the session.
create table lcw.widgets (
  widget_unique_id uuid        not null default gen_random_uuid(),
  compartment      text,
  name             text        not null,
  created_at       timestamptz not null default now(),
  created_by       text        not null,
  constraint pk_widgets primary key (widget_unique_id)
);

alter table lcw.widgets enable row level security;
alter table lcw.widgets force row level security;

create policy pl_widgets_space on lcw.widgets
  using (
    case current_setting('stellaegis.space', true)
      when '_shared' then compartment is null
      else compartment = current_setting('stellaegis.space', true)
    end)
  with check (
    case current_setting('stellaegis.space', true)
      when '_shared' then compartment is null
      else compartment = current_setting('stellaegis.space', true)
    end);
