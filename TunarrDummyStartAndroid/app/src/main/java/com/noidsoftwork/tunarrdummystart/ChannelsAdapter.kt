package com.noidsoftwork.tunarrdummystart

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.RecyclerView
import com.noidsoftwork.tunarrdummystart.databinding.ItemChannelBinding

class ChannelsAdapter(private var items: List<ChannelStatusUpdate> = emptyList()) :
    RecyclerView.Adapter<ChannelsAdapter.ViewHolder>() {

    class ViewHolder(val binding: ItemChannelBinding) : RecyclerView.ViewHolder(binding.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemChannelBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val item = items[position]
        val context = holder.itemView.context
        
        holder.binding.tvChannelId.text = "Channel ${item.channel}"
        holder.binding.tvChannelState.text = item.state
        holder.binding.tvChannelMessage.text = item.message
        holder.binding.tvChannelDuration.text = "Duration: ${item.runDuration ?: "00:00:00"}"
        holder.binding.tvChannelAttempts.text = "Attempts: ${item.attempt}/${item.maxAttempts}"

        val colorRes = when (item.state) {
            "Connected" -> R.color.state_connected
            "Connecting" -> R.color.state_connecting
            "Retrying" -> R.color.state_retrying
            "Disabled" -> R.color.state_disabled
            "Idle" -> R.color.state_idle
            else -> R.color.state_failed
        }
        val color = ContextCompat.getColor(context, colorRes)

        holder.binding.channelStateIndicator.setBackgroundColor(color)
        holder.binding.tvChannelState.setTextColor(color)
    }

    override fun getItemCount() = items.size

    fun updateItems(newItems: List<ChannelStatusUpdate>) {
        items = newItems
        notifyDataSetChanged()
    }
}
