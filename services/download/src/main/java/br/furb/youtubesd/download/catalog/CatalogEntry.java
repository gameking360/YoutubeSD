package br.furb.youtubesd.download.catalog;

import java.util.List;

public record CatalogEntry(
        String videoId,
        String ownerId,
        String title,
        Visibility visibility,
        int durationSec,
        String hlsBucket,
        String masterKey,
        List<Rendition> renditions) {

    public String hlsPrefix() {
        int lastSlash = masterKey.lastIndexOf('/');
        return lastSlash < 0 ? "" : masterKey.substring(0, lastSlash + 1);
    }
}
