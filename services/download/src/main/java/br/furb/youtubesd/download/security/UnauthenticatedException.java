package br.furb.youtubesd.download.security;

public class UnauthenticatedException extends RuntimeException {

    public UnauthenticatedException() {
        super("Usuário não identificado.");
    }
}
