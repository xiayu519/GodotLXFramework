extends SceneTree

# Exercise the fixed main entry with a configured product, without loading game content.
var invalid_status: PackedScene

func _initialize() -> void:
    if "--lx-bootstrap-probe-failure" in OS.get_cmdline_user_args():
        invalid_status = PackedScene.new()
        var invalid_root := Node.new()
        invalid_root.name = "ExpectedInvalidStatus"
        assert(invalid_status.pack(invalid_root) == OK)
        invalid_root.free()
        invalid_status.take_over_path("res://scene/ui/framework_status.tscn")
        print("LX_BOOTSTRAP_FAILURE_PROBE_ARMED")
    var main: PackedScene = load("res://scene/main.tscn")
    var host := main.instantiate()
    host.set("InitialWorldId", "validation.unregistered_product_world")
    host.set("InitialWorldScene", "res://validation/unregistered_product_world.tscn")
    root.add_child.call_deferred(host)
