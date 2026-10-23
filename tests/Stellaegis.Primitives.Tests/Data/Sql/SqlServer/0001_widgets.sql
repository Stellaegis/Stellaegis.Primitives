-- The widgets of the life-cycle tests on SQL Server. The view needs a batch of its own: the go line splits them.
create table lcw.widgets (
  widget_id   bigint            not null identity (1, 1),
  compartment nvarchar(63)      null,
  name        nvarchar(100)     not null,
  created_at  datetimeoffset(3) not null
    constraint df_widgets_created_at default sysdatetimeoffset(),
  created_by  nvarchar(128)     not null,
  constraint pk_widgets primary key (widget_id)
);
go

create view lcw.v_widget_names
as
select widget_id, name from lcw.widgets;
