-- Keeps each actor to the widgets it created, within its space. Replayable.
drop policy if exists pl_widgets_owner on lcw.widgets;

create policy pl_widgets_owner on lcw.widgets
  as restrictive
  using (created_by = current_setting('stellaegis.actor', true))
  with check (created_by = current_setting('stellaegis.actor', true));
