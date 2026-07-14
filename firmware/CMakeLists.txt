target_sources_ifdef(CONFIG_ZMK_COMPANION_DISPLAY app PRIVATE
    ${CMAKE_CURRENT_LIST_DIR}/custom_status_screen.c
    ${CMAKE_CURRENT_LIST_DIR}/mono_16.c
    ${CMAKE_CURRENT_LIST_DIR}/mono_8.c
    ${CMAKE_CURRENT_LIST_DIR}/mono_icon.c)
