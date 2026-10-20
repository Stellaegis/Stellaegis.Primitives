-- A second script, so that the order and the contiguity of the versions are exercised.
create table lcw.widget_tags (
  widget_unique_id uuid not null references lcw.widgets (widget_unique_id) on delete cascade,
  tag              text not null,
  constraint pk_widget_tags primary key (widget_unique_id, tag)
);
