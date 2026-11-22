package br.furb.youtubesd.download.catalog;

import java.util.List;

public record PageResponse<T>(List<T> items, int page, int size, boolean hasNext) {

    public static <T> PageResponse<T> of(List<T> fetchedWithOneExtra, int page, int size) {
        boolean hasNext = fetchedWithOneExtra.size() > size;
        List<T> items = hasNext ? fetchedWithOneExtra.subList(0, size) : fetchedWithOneExtra;
        return new PageResponse<>(List.copyOf(items), page, size, hasNext);
    }
}
