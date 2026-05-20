using System;
using System.Collections.Generic;
using System.Text;

namespace Vora.Application.Playlists.ViewModels;

public class UpdatePlaylistRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}