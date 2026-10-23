-- A second script, so that the order and the contiguity of the versions are exercised.
create table lcw.widget_tags (
  widget_id bigint        not null,
  tag       nvarchar(50)  not null,
  constraint pk_widget_tags primary key (widget_id, tag),
  constraint fk_widget_tags_widgets foreign key (widget_id) references lcw.widgets (widget_id) on delete cascade
);
