namespace MonAlbumPc.Core.Media;

/// <summary>
/// The visible area of a map, in decimal degrees. Longitudes may go past ±180° when the map wraps around the world
/// (Leaflet keeps counting: a view across the date line runs from 170° to 190°).
/// </summary>
public readonly record struct GeoBounds(double South, double West, double North, double East)
{
    public bool Contains(double latitude, double longitude)
    {
        if (latitude < South || latitude > North)
        {
            return false;
        }
        if (East - West >= 360)
        {
            return true;
        }
        // How far east of West the longitude is, once brought into the same world copy.
        var offset = (longitude - West) % 360;
        if (offset < 0)
        {
            offset += 360;
        }
        return offset <= East - West;
    }
}
