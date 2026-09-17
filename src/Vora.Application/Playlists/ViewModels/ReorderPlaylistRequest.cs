using System;
using System.Collections.Generic;
using System.Text;

namespace Vora.Application.Playlists.ViewModels;

public class ReorderPlaylistRequest
{
    public List<Guid> PlaylistItemIds { get; set; } = new();
}