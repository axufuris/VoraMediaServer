using System;
using System.Collections.Generic;
using System.Text;
using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Playlists.ViewModels;

public class PlaylistDetailsVM : PlaylistSummaryVM
{
    public List<PlaylistItemVM> Items { get; set; } = new();
}