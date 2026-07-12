fn main() {
    // Embed the application manifest (requireAdministrator + common controls v6).
    embed_resource::compile("winadmin-ctl.rc", embed_resource::NONE);
}
