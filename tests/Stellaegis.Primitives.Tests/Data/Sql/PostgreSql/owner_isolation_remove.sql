-- Removes the isolation by owner. Replayable.
drop policy if exists pl_widgets_owner on lcw.widgets;
